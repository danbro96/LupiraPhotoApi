using LupiraPhotoApi.Core.Application.Results;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Marten;
using Marten.Linq.MatchesSql;

namespace LupiraPhotoApi.Core.Application;

public sealed class PhotoQueryService(IQuerySession session, PhotoPresigner presigner)
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;
    public const int MapLimit = 2000;

    public async Task<OpResult<PhotoListResponse>> ListAsync(
        Guid principalId, DateTimeOffset? from, DateTimeOffset? to, Bbox? bbox,
        AssetKind? kind, AssetStatus? status, int? limit, string? cursor, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var query = session.Query<PhotoAsset>().Where(a => a.PrincipalId == principalId);
        if (from is { } f) query = query.Where(a => a.TakenAt >= f);
        if (to is { } t) query = query.Where(a => a.TakenAt <= t);
        if (bbox is { } b)
            query = query.Where(a => a.Latitude >= b.MinLat && a.Latitude <= b.MaxLat
                                  && a.Longitude >= b.MinLon && a.Longitude <= b.MaxLon);
        if (kind is { } k) query = query.Where(a => a.Kind == k);
        if (status is { } s) query = query.Where(a => a.Status == s);

        if (cursor is not null)
        {
            if (!PageCursor.TryParse(cursor, out var c))
                return OpResult<PhotoListResponse>.Invalid("Malformed cursor.");
            // Postgres row-value comparison = keyset "strictly after the cursor" in (TakenAt, Id) DESC order.
            // Raw SQL because LINQ can't express a Guid tie-break Marten translates.
            query = query.Where(a => a.MatchesSql("(d.taken_at, d.id) < (?, ?)", c.TakenAt, c.Id));
        }

        var page = await query
            .OrderByDescending(a => a.TakenAt).ThenByDescending(a => a.Id)
            .Take(take + 1)
            .ToListAsync(ct);

        var hasMore = page.Count > take;
        var items = new List<PhotoListItemDto>(Math.Min(page.Count, take));
        foreach (var asset in page.Take(take))
            items.Add(await ToListItemAsync(asset, ct));

        return OpResult<PhotoListResponse>.Ok(new PhotoListResponse
        {
            Items = items,
            NextCursor = hasMore ? PageCursor.Format(page[take - 1].TakenAt, page[take - 1].Id) : null,
        });
    }

    public async Task<OpResult<PhotoMapResponse>> MapAsync(
        Guid principalId, Bbox bbox, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var query = session.Query<PhotoAsset>()
            .Where(a => a.PrincipalId == principalId && a.Status == AssetStatus.Ready)
            .Where(a => a.Latitude >= bbox.MinLat && a.Latitude <= bbox.MaxLat
                     && a.Longitude >= bbox.MinLon && a.Longitude <= bbox.MaxLon);
        if (from is { } f) query = query.Where(a => a.TakenAt >= f);
        if (to is { } t) query = query.Where(a => a.TakenAt <= t);

        var assets = await query.OrderByDescending(a => a.TakenAt).Take(MapLimit).ToListAsync(ct);
        var features = new List<PhotoMapFeatureDto>(assets.Count);
        foreach (var asset in assets)
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
        ThumbUrl = await presigner.ThumbUrlAsync(asset, ct),
    };
}
