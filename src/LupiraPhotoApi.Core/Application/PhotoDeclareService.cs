using LupiraPhotoApi.Domain;
using LupiraPhotoApi.Dtos.Photos;
using LupiraPhotoApi.Storage;
using Marten;
using Microsoft.Extensions.Options;

namespace LupiraPhotoApi.Application;

public sealed class PhotoDeclareService(IDocumentSession session, IObjectStore store, IOptions<PhotoOptions> options)
{
    private readonly PhotoOptions _opts = options.Value;

    public async Task<OpResult<DeclaredPhotoResponse>> DeclareAsync(Guid principalId, DeclarePhotoRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.DeviceId) || string.IsNullOrWhiteSpace(req.MediaStoreId))
            return OpResult<DeclaredPhotoResponse>.Invalid("DeviceId and MediaStoreId are required.");
        if (!ObjectKeys.TryResolve(req.ContentType, out var extension, out var kind))
            return OpResult<DeclaredPhotoResponse>.Invalid($"Unsupported content type '{req.ContentType}'.");
        if (req.SizeBytes <= 0 || req.SizeBytes > _opts.MaxSizeBytes)
            return OpResult<DeclaredPhotoResponse>.Invalid($"SizeBytes must be within (0, {_opts.MaxSizeBytes}].");
        if (req.Latitude is < -90 or > 90 || req.Longitude is < -180 or > 180 || req.Latitude.HasValue != req.Longitude.HasValue)
            return OpResult<DeclaredPhotoResponse>.Invalid("Latitude/Longitude must be a valid pair or both absent.");

        var id = AssetId(principalId, req.DeviceId, req.MediaStoreId);
        var asset = await session.LoadAsync<PhotoAsset>(id, ct);
        if (asset is null)
        {
            asset = new PhotoAsset
            {
                Id = id,
                PrincipalId = principalId,
                DeviceId = req.DeviceId,
                MediaStoreId = req.MediaStoreId,
                Kind = kind,
                Status = AssetStatus.Declared,
                TakenAt = req.TakenAt,
                Latitude = req.Latitude,
                Longitude = req.Longitude,
                ContentType = req.ContentType,
                SizeBytes = req.SizeBytes,
                Width = req.Width,
                Height = req.Height,
                DurationSeconds = req.DurationSeconds,
                Sha256 = req.Sha256,
                OriginalKey = ObjectKeys.Original(principalId, req.TakenAt, id, extension),
                ThumbKey = null,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            session.Store(asset);
            await session.SaveChangesAsync(ct);
        }

        // Beyond Declared the bytes are already in — return status only so the client skips the transfer.
        if (asset.Status != AssetStatus.Declared)
            return OpResult<DeclaredPhotoResponse>.Ok(new DeclaredPhotoResponse
            {
                AssetId = asset.Id,
                Status = asset.Status,
                RequiredHeaders = [],
            });

        var expiry = TimeSpan.FromMinutes(_opts.PresignPutExpiryMinutes);
        var url = await store.PresignPutAsync(asset.OriginalKey, asset.ContentType, expiry, ct);
        return OpResult<DeclaredPhotoResponse>.Ok(new DeclaredPhotoResponse
        {
            AssetId = asset.Id,
            Status = asset.Status,
            UploadUrl = url.ToString(),
            UploadExpiresAt = DateTimeOffset.UtcNow + expiry,
            // Content-Type rides the signature — a PUT without it (or with another value) fails auth.
            RequiredHeaders = new Dictionary<string, string> { ["Content-Type"] = asset.ContentType },
        });
    }

    internal static Guid AssetId(Guid principalId, string deviceId, string mediaStoreId) =>
        DeterministicGuid.From($"photo:{principalId:N}:{deviceId}:{mediaStoreId}");
}
