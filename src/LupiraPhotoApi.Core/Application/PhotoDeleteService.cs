using LupiraPhotoApi.Domain;
using LupiraPhotoApi.Storage;
using Marten;

namespace LupiraPhotoApi.Application;

public sealed class PhotoDeleteService(IDocumentSession session, IObjectStore store)
{
    /// <summary>Objects first, document last — a crash in between leaves a doc pointing at deleted
    /// bytes (harmless, retryable) rather than orphaned bytes nothing references.</summary>
    public async Task<OpResult> DeleteAsync(Guid principalId, Guid assetId, CancellationToken ct)
    {
        var asset = await session.LoadAsync<PhotoAsset>(assetId, ct);
        if (asset is null || asset.PrincipalId != principalId) return OpResult.NotFound();

        await store.DeleteAsync(asset.OriginalKey, ct);
        if (asset.ThumbKey is { } thumbKey) await store.DeleteAsync(thumbKey, ct);

        session.Delete<PhotoAsset>(assetId);
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }
}
