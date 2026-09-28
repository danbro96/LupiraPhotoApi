using LupiraPhotoApi.Core.Domain;
using Marten;

namespace LupiraPhotoApi.Core.Application;

/// <summary>One-off library fixes run from the CLI.</summary>
public sealed class PhotoMaintenanceService(IDocumentSession session)
{
    private const string ImportDevicePrefix = "import:";

    /// <summary>Phone uploads from before the phone sent its owner's contact.</summary>
    public async Task<int> BackfillCapturedByAsync(Guid principalId, Guid contactId, CancellationToken ct)
    {
        var assets = await session.Query<PhotoAsset>()
            .Where(a => a.PrincipalId == principalId && a.CapturedBySource == null && !a.DeviceId.StartsWith(ImportDevicePrefix))
            .ToListAsync(ct);
        foreach (var asset in assets.Where(a => Photographer.Offer(a, contactId, CapturedBySource.Uploader)))
            session.Store(asset);
        await session.SaveChangesAsync(ct);
        return assets.Count;
    }

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
