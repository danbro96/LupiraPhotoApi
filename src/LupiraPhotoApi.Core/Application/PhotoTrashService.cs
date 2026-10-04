using Lupira.Results;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Marten;

namespace LupiraPhotoApi.Core.Application;

/// <summary>Trash and restore. Permanent removal — emptying the trash, the retention sweep — is
/// <see cref="PhotoDeleteService"/>'s.</summary>
public sealed class PhotoTrashService(IDocumentSession session, PhotoPresigner presigner)
{
    public Task<OpResult<PhotoAssetDto>> TrashAsync(Guid principalId, Guid assetId, CancellationToken ct) =>
        ApplyAsync(principalId, assetId, asset => AssetTrash.TryTrash(asset, DateTimeOffset.UtcNow), ct);

    public Task<OpResult<PhotoAssetDto>> RestoreAsync(Guid principalId, Guid assetId, CancellationToken ct) =>
        ApplyAsync(principalId, assetId, AssetTrash.TryRestore, ct);

    private async Task<OpResult<PhotoAssetDto>> ApplyAsync(
        Guid principalId, Guid assetId, Func<PhotoAsset, bool> change, CancellationToken ct)
    {
        var asset = await session.LoadAsync<PhotoAsset>(assetId, ct);
        if (asset is null || asset.PrincipalId != principalId) return OpResult<PhotoAssetDto>.NotFound();
        if (change(asset))
        {
            session.Store(asset);
            await session.SaveChangesAsync(ct);
        }

        return OpResult<PhotoAssetDto>.Ok(await presigner.ToDtoAsync(asset, includeOriginal: false, ct));
    }
}
