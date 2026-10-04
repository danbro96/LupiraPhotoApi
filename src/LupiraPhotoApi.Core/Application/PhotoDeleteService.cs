using System.Linq.Expressions;
using Lupira.Results;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Storage;
using Marten;

namespace LupiraPhotoApi.Core.Application;

public sealed class PhotoDeleteService(IDocumentSession session, IObjectStore store, PhotoPresigner presigner)
{
    // Committed per batch, so an interrupted sweep keeps its progress and a retry resumes it.
    private const int PurgeBatchSize = 100;

    public async Task<OpResult> DeleteAsync(Guid principalId, Guid assetId, CancellationToken ct)
    {
        var asset = await session.LoadAsync<PhotoAsset>(assetId, ct);
        if (asset is null || asset.PrincipalId != principalId) return OpResult.NotFound();

        await PurgeAsync([asset], ct);
        return OpResult.Ok();
    }

    public Task<int> EmptyTrashAsync(Guid principalId, CancellationToken ct) =>
        PurgeWhereAsync(a => a.PrincipalId == principalId && a.TrashedAt != null, ct);

    /// <summary>Every principal's assets trashed before <paramref name="cutoff"/>.</summary>
    public Task<int> PurgeTrashedBeforeAsync(DateTimeOffset cutoff, CancellationToken ct) =>
        PurgeWhereAsync(a => a.TrashedAt != null && a.TrashedAt < cutoff, ct);

    private async Task<int> PurgeWhereAsync(Expression<Func<PhotoAsset, bool>> filter, CancellationToken ct)
    {
        var purged = 0;
        while (true)
        {
            var batch = await session.Query<PhotoAsset>().Where(filter).Take(PurgeBatchSize).ToListAsync(ct);
            if (batch.Count == 0) return purged;
            await PurgeAsync(batch, ct);
            purged += batch.Count;
        }
    }

    /// <summary>Objects first, document last — a crash in between leaves a doc pointing at deleted
    /// bytes (harmless, retryable) rather than orphaned bytes nothing references.</summary>
    private async Task PurgeAsync(IReadOnlyList<PhotoAsset> assets, CancellationToken ct)
    {
        foreach (var asset in assets)
        {
            await store.DeleteAsync(asset.OriginalKey, ct);
            if (asset.ThumbKey is { } thumbKey) await store.DeleteAsync(thumbKey, ct);
            presigner.Forget(asset);

            var assetId = asset.Id;
            session.Delete<PhotoAsset>(assetId);
            // Pointers to bytes that no longer exist. They can't be promoted — a duplicate never had its own.
            session.DeleteWhere<PhotoAsset>(a => a.DuplicateOfId == assetId);
        }

        await session.SaveChangesAsync(ct);
    }
}
