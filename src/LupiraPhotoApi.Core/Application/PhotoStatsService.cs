using LupiraPhotoApi.Core.Domain;
using Marten;

namespace LupiraPhotoApi.Core.Application;

public sealed class PhotoStatsService(IQuerySession session)
{
    public async Task<PhotoStats> GetAsync(Guid principalId, CancellationToken ct)
    {
        // Family-scale library: pull the thin projection and aggregate in memory.
        var assets = await session.Query<PhotoAsset>()
            .Where(a => a.PrincipalId == principalId)
            .Select(a => new { a.Kind, a.Status, a.GeotagSource, a.SizeBytes, a.TakenAt, a.Camera })
            .ToListAsync(ct);

        return new PhotoStats
        {
            TotalAssets = assets.Count,
            TotalBytes = assets.Sum(a => a.SizeBytes),
            ByKind = assets.CountBy(a => a.Kind.ToString()).ToDictionary(),
            ByStatus = assets.CountBy(a => a.Status.ToString()).ToDictionary(),
            ByGeotagSource = assets.CountBy(a => a.GeotagSource.ToString()).ToDictionary(),
            ByMonth = assets.CountBy(a => a.TakenAt.UtcDateTime.ToString("yyyy-MM")).OrderBy(kv => kv.Key).ToDictionary(),
            ByCamera = assets.Select(a => DtoMapping.CameraName(a.Camera)).OfType<string>().CountBy(n => n).OrderByDescending(kv => kv.Value).ToDictionary(),
        };
    }
}
