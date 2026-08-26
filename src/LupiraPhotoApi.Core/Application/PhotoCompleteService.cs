using LupiraPhotoApi.Core.Application.Results;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using LupiraPhotoApi.Core.Storage;
using Marten;

namespace LupiraPhotoApi.Core.Application;

public sealed class PhotoCompleteService(IDocumentSession session, IObjectStore store, PhotoPresigner presigner)
{
    public async Task<OpResult<PhotoAssetDto>> CompleteAsync(Guid principalId, Guid assetId, CancellationToken ct)
    {
        var asset = await session.LoadAsync<PhotoAsset>(assetId, ct);
        if (asset is null || asset.PrincipalId != principalId) return OpResult<PhotoAssetDto>.NotFound();

        if (asset.Status == AssetStatus.Declared)
        {
            var stat = await store.HeadAsync(asset.OriginalKey, ct);
            if (stat is null)
                return OpResult<PhotoAssetDto>.Conflict("Object not found — upload the bytes before completing.");
            if (stat.SizeBytes != asset.SizeBytes)
                return OpResult<PhotoAssetDto>.Conflict($"Uploaded size {stat.SizeBytes} does not match declared {asset.SizeBytes}.");

            AssetLifecycle.TryMarkUploaded(asset, DateTimeOffset.UtcNow);
            session.Store(asset);
            await session.SaveChangesAsync(ct);
        }

        // Already Uploaded/Processing/Ready: idempotent — report the current state.
        return OpResult<PhotoAssetDto>.Ok(await presigner.ToDtoAsync(asset, includeOriginal: false, ct));
    }

    public async Task<OpResult<PhotoAssetDto>> ReprocessAsync(Guid principalId, Guid assetId, CancellationToken ct)
    {
        var asset = await session.LoadAsync<PhotoAsset>(assetId, ct);
        if (asset is null || asset.PrincipalId != principalId) return OpResult<PhotoAssetDto>.NotFound();
        if (!AssetLifecycle.TryReprocess(asset))
            return OpResult<PhotoAssetDto>.Conflict($"Asset in status {asset.Status} cannot be reprocessed.");
        session.Store(asset);
        await session.SaveChangesAsync(ct);
        return OpResult<PhotoAssetDto>.Ok(await presigner.ToDtoAsync(asset, includeOriginal: false, ct));
    }
}
