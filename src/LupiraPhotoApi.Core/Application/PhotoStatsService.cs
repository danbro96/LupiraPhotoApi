using LupiraPhotoApi.Core.Application.Results;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Marten;

namespace LupiraPhotoApi.Core.Application;

public sealed class PhotoStatsService(IQuerySession session)
{
    public const int DefaultPlaceLimit = 10;
    public const int MaxPlaceLimit = 50;

    public async Task<PhotoStats> GetAsync(Guid principalId, CancellationToken ct)
    {
        // Family-scale library: pull the thin projection and aggregate in memory.
        var all = await session.Query<PhotoAsset>()
            .Where(a => a.PrincipalId == principalId)
            .Select(a => new { a.Kind, a.Status, a.GeotagSource, a.SizeBytes, a.TakenAt, a.Camera, a.TrashedAt })
            .ToListAsync(ct);
        var assets = all.Where(a => a.TrashedAt is null).ToList();

        return new PhotoStats
        {
            TotalAssets = assets.Count,
            TotalBytes = assets.Sum(a => a.SizeBytes),
            TrashedAssets = all.Count - assets.Count,
            ByKind = assets.CountBy(a => a.Kind.ToString()).ToDictionary(),
            ByStatus = assets.CountBy(a => a.Status.ToString()).ToDictionary(),
            ByGeotagSource = assets.CountBy(a => a.GeotagSource.ToString()).ToDictionary(),
            ByMonth = assets.CountBy(a => a.TakenAt.UtcDateTime.ToString("yyyy-MM")).OrderBy(kv => kv.Key).ToDictionary(),
            ByCamera = assets.Select(a => DtoMapping.CameraName(a.Camera)).OfType<string>().CountBy(n => n).OrderByDescending(kv => kv.Value).ToDictionary(),
        };
    }

    /// <summary>Place-filter suggestions: <paramref name="q"/> matches exactly as the list's <c>place</c> filter does.</summary>
    public async Task<List<PhotoPlaceCount>> PlacesAsync(Guid principalId, string? q, int? limit, CancellationToken ct)
    {
        var query = session.Query<PhotoAsset>()
            .Where(a => a.PrincipalId == principalId && a.TrashedAt == null && a.Status != AssetStatus.Duplicate && a.PlaceLabel != null);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(a => a.PlaceLabel!.Contains(q, StringComparison.OrdinalIgnoreCase));

        return RankPlaces(await query.Select(a => a.PlaceLabel!).ToListAsync(ct), limit);
    }

    internal static List<PhotoPlaceCount> RankPlaces(IEnumerable<string> labels, int? limit) =>
        labels.CountBy(label => label)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(Math.Clamp(limit ?? DefaultPlaceLimit, 1, MaxPlaceLimit))
            .Select(kv => new PhotoPlaceCount { Label = kv.Key, Count = kv.Value })
            .ToList();

    /// <summary>Measured locations only: a Folder geotag is the import folder's assumed place, not where the photo was taken.</summary>
    public async Task<OpResult<List<PhotoDensityCellDto>>> DensityAsync(
        Guid principalId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        if (from > to)
            return OpResult<List<PhotoDensityCellDto>>.Invalid("from must not be after to.");

        var query = session.Query<PhotoAsset>()
            .Where(a => a.PrincipalId == principalId && a.Status == AssetStatus.Ready && a.TrashedAt == null
                     && a.Latitude != null && a.Longitude != null && a.GeotagSource != GeotagSource.Folder);
        if (from is { } f) query = query.Where(a => a.TakenAt >= f);
        if (to is { } t) query = query.Where(a => a.TakenAt <= t);

        var rows = await query.Select(a => new { a.Latitude, a.Longitude, a.TakenAt }).ToListAsync(ct);
        return OpResult<List<PhotoDensityCellDto>>.Ok(Aggregate(rows.Select(r => (r.Latitude!.Value, r.Longitude!.Value, r.TakenAt))));
    }

    internal static List<PhotoDensityCellDto> Aggregate(IEnumerable<(double Latitude, double Longitude, DateTimeOffset TakenAt)> rows) =>
        rows.GroupBy(r => (Latitude: Math.Round(r.Latitude, 3), Longitude: Math.Round(r.Longitude, 3)))
            .Select(g => new PhotoDensityCellDto
            {
                Latitude = g.Key.Latitude,
                Longitude = g.Key.Longitude,
                Count = g.Count(),
                Days = g.Select(r => DateOnly.FromDateTime(r.TakenAt.UtcDateTime)).Distinct().Order().ToList(),
            })
            .OrderByDescending(c => c.Days.Count)
            .ThenByDescending(c => c.Count)
            .ThenBy(c => c.Latitude)
            .ThenBy(c => c.Longitude)
            .ToList();
}
