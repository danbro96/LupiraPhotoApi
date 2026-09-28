using LupiraPhotoApi.Core.Application.Results;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Marten;

namespace LupiraPhotoApi.Core.Application;

/// <summary>Hand-set metadata. Manual outranks every derived source, so an import re-run never undoes it.</summary>
public sealed class PhotoCurationService(IDocumentSession session, PhotoPresigner presigner)
{
    public async Task<OpResult<PhotoAssetDto>> UpdateAsync(Guid principalId, Guid assetId, UpdatePhotoRequest req, CancellationToken ct)
    {
        var asset = await session.LoadAsync<PhotoAsset>(assetId, ct);
        if (asset is null || asset.PrincipalId != principalId) return OpResult<PhotoAssetDto>.NotFound();

        Photographer.SetManual(asset, req.CapturedByContactId);
        session.Store(asset);
        await session.SaveChangesAsync(ct);
        return OpResult<PhotoAssetDto>.Ok(await presigner.ToDtoAsync(asset, includeOriginal: false, ct));
    }
}
