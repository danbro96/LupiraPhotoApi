using LupiraPhotoApi.Core.Domain;
using Marten;

namespace LupiraPhotoApi.Core.Application;

/// <summary>One-off library fixes run from the CLI.</summary>
public sealed class PhotoMaintenanceService(IDocumentSession session)
{
    /// <summary>Re-queues finished assets so processing re-derives camera, geotag and label.</summary>
    public async Task<int> ReprocessAllAsync(Guid principalId, CancellationToken ct)
    {
        var assets = await session.Query<PhotoAsset>()
            .Where(a => a.PrincipalId == principalId && (a.Status == AssetStatus.Ready || a.Status == AssetStatus.Failed))
            .ToListAsync(ct);
        var queued = 0;
        foreach (var asset in assets.Where(AssetLifecycle.TryReprocess))
        {
            session.Store(asset);
            queued++;
        }

        await session.SaveChangesAsync(ct);
        return queued;
    }
}
