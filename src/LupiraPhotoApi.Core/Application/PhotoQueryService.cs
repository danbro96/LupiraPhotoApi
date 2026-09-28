using LupiraPhotoApi.Core.Application.Results;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Marten;
using Marten.Linq.MatchesSql;
using Microsoft.Extensions.Options;

namespace LupiraPhotoApi.Core.Application;

public sealed class PhotoQueryService(IQuerySession session, PhotoPresigner presigner, IOptions<PhotoOptions> options)
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;
    public const int MapLimit = 2000;
    public const int LookupMax = 200;

    private readonly TimeSpan _trashRetention = TimeSpan.FromDays(options.Value.TrashRetentionDays);

    public async Task<OpResult<PhotoListResponse>> ListAsync(
        Guid principalId, DateTimeOffset? from, DateTimeOffset? to, Bbox? bbox,
        AssetKind? kind, AssetStatus? status, bool? located, string? place, string? sourceAlbum, bool? trashed,
        PhotoSort? sort, int? limit, string? cursor, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var order = sort ?? PhotoSort.TakenAtDesc;
        var query = session.Query<PhotoAsset>().Where(a => a.PrincipalId == principalId);
        query = trashed == true ? query.Where(a => a.TrashedAt != null) : query.Where(a => a.TrashedAt == null);
        if (from is { } f) query = query.Where(a => a.TakenAt >= f);
        if (to is { } t) query = query.Where(a => a.TakenAt <= t);
        if (bbox is { } b)
        {
            query = query.Where(a => a.Latitude >= b.MinLat && a.Latitude <= b.MaxLat
                                  && a.Longitude >= b.MinLon && a.Longitude <= b.MaxLon);
        }

        if (kind is { } k) query = query.Where(a => a.Kind == k);
        // Duplicates are the same photo twice — never in a default listing, only when asked for by name.
        query = status is { } s ? query.Where(a => a.Status == s) : query.Where(a => a.Status != AssetStatus.Duplicate);
        if (located is { } geotagged)
            query = geotagged ? query.Where(a => a.Latitude != null) : query.Where(a => a.Latitude == null);
        if (!string.IsNullOrWhiteSpace(place))
            query = query.Where(a => a.PlaceLabel != null && a.PlaceLabel.Contains(place, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(sourceAlbum))
            query = query.Where(a => a.SourceAlbum == sourceAlbum);

        if (cursor is not null)
        {
            if (!PageCursor.TryParse(cursor, order, out var c))
                return OpResult<PhotoListResponse>.Invalid("Malformed cursor, or a cursor from a different sort order.");
            // Postgres row-value comparison = keyset "strictly after the cursor" in (TakenAt, Id) order.
            // Raw SQL because LINQ can't express a Guid tie-break Marten translates. The operator has to
            // match the ORDER BY below or paging silently skips and repeats rows.
            query = order == PhotoSort.TakenAtDesc
                ? query.Where(a => a.MatchesSql("(d.taken_at, d.id) < (?, ?)", c.TakenAt, c.Id))
                : query.Where(a => a.MatchesSql("(d.taken_at, d.id) > (?, ?)", c.TakenAt, c.Id));
        }

        var ordered = order == PhotoSort.TakenAtDesc
            ? query.OrderByDescending(a => a.TakenAt).ThenByDescending(a => a.Id)
            : query.OrderBy(a => a.TakenAt).ThenBy(a => a.Id);

        var page = await ordered.Take(take + 1).ToListAsync(ct);

        var hasMore = page.Count > take;
        var items = new List<PhotoListItemDto>(Math.Min(page.Count, take));
        foreach (var asset in page.Take(take))
            items.Add(await ToListItemAsync(asset, ct));

        return OpResult<PhotoListResponse>.Ok(new PhotoListResponse
        {
            Items = items,
            NextCursor = hasMore ? PageCursor.Format(order, page[take - 1].TakenAt, page[take - 1].Id) : null,
        });
    }

    /// <summary>Hydrates a set of ids in one round trip — how a caller turns relation references (or any
    /// other id list) into renderable items. Owner-scoped; unknown and trashed ids are simply absent.</summary>
    public async Task<OpResult<PhotoListResponse>> LookupAsync(Guid principalId, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count > LookupMax)
            return OpResult<PhotoListResponse>.Invalid($"At most {LookupMax} ids per lookup.");
        if (ids.Count == 0)
            return OpResult<PhotoListResponse>.Ok(new PhotoListResponse { Items = [] });

        var assets = await session.Query<PhotoAsset>()
            .Where(a => a.PrincipalId == principalId && a.TrashedAt == null && ids.Contains(a.Id))
            .OrderByDescending(a => a.TakenAt)
            .ToListAsync(ct);

        var items = new List<PhotoListItemDto>(assets.Count);
        foreach (var asset in assets) items.Add(await ToListItemAsync(asset, ct));
        return OpResult<PhotoListResponse>.Ok(new PhotoListResponse { Items = items });
    }

    public async Task<OpResult<PhotoMapResponse>> MapAsync(
        Guid principalId, Bbox bbox, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var query = session.Query<PhotoAsset>()
            .Where(a => a.PrincipalId == principalId && a.Status == AssetStatus.Ready && a.TrashedAt == null)
            .Where(a => a.Latitude >= bbox.MinLat && a.Latitude <= bbox.MaxLat
                     && a.Longitude >= bbox.MinLon && a.Longitude <= bbox.MaxLon);
        if (from is { } f) query = query.Where(a => a.TakenAt >= f);
        if (to is { } t) query = query.Where(a => a.TakenAt <= t);

        var assets = await query.OrderByDescending(a => a.TakenAt).Take(MapLimit).ToListAsync(ct);
        var features = new List<PhotoMapFeatureDto>(assets.Count);
        foreach (var asset in assets)
        {
            features.Add(new PhotoMapFeatureDto
            {
                Geometry = new PhotoMapPointDto { Coordinates = [asset.Longitude!.Value, asset.Latitude!.Value] },
                Properties = new PhotoMapPropertiesDto
                {
                    Id = asset.Id,
                    Kind = asset.Kind,
                    TakenAt = asset.TakenAt,
                    PlaceLabel = asset.PlaceLabel,
                    ThumbUrl = await presigner.ThumbUrlAsync(asset, ct),
                },
            });
        }

        return OpResult<PhotoMapResponse>.Ok(new PhotoMapResponse { Features = features });
    }

    public async Task<OpResult<PhotoAssetDto>> GetAsync(Guid principalId, Guid assetId, CancellationToken ct)
    {
        var asset = await session.LoadAsync<PhotoAsset>(assetId, ct);
        if (asset is null || asset.PrincipalId != principalId) return OpResult<PhotoAssetDto>.NotFound();
        return OpResult<PhotoAssetDto>.Ok(await presigner.ToDtoAsync(asset, includeOriginal: true, ct));
    }

    private async Task<PhotoListItemDto> ToListItemAsync(PhotoAsset asset, CancellationToken ct) => new()
    {
        Id = asset.Id,
        Kind = asset.Kind,
        Status = asset.Status,
        TakenAt = asset.TakenAt,
        Latitude = asset.Latitude,
        Longitude = asset.Longitude,
        PlaceLabel = asset.PlaceLabel,
        Width = asset.Width,
        Height = asset.Height,
        DurationSeconds = asset.DurationSeconds,
        GeotagSource = asset.GeotagSource,
        ContentType = asset.ContentType,
        SizeBytes = asset.SizeBytes,
        LastError = asset.LastError,
        DuplicateOfId = asset.DuplicateOfId,
        Camera = DtoMapping.Camera(asset.Camera),
        TakenAtSource = asset.TakenAtSource,
        CapturedByContactId = asset.CapturedByContactId,
        CapturedBySource = asset.CapturedBySource,
        SourceAlbum = asset.SourceAlbum,
        TrashedAt = asset.TrashedAt,
        PurgesAt = AssetTrash.PurgesAt(asset, _trashRetention),
        ThumbUrl = await presigner.ThumbUrlAsync(asset, ct),
    };
}
