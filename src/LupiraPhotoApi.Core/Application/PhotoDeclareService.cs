using LupiraPhotoApi.Core.Application.Results;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using LupiraPhotoApi.Core.Storage;
using Marten;
using Microsoft.Extensions.Options;

namespace LupiraPhotoApi.Core.Application;

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
            var canonical = await FindCanonicalAsync(principalId, id, req, ct);
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
                OriginalKey = ObjectKeys.Original(principalId, req.TakenAt, id, extension),
                ThumbKey = null,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            if (canonical is { } original) AssetLifecycle.TryMarkDuplicate(asset, original, DateTimeOffset.UtcNow);
            session.Store(asset);
            await session.SaveChangesAsync(ct);
        }

        // Beyond Declared there is nothing to send: the bytes are already in, or another asset holds them.
        if (asset.Status != AssetStatus.Declared)
        {
            return OpResult<DeclaredPhotoResponse>.Ok(new DeclaredPhotoResponse
            {
                AssetId = asset.Id,
                Status = asset.Status,
                RequiredHeaders = [],
            });
        }

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

    /// <summary>Byte-identical copies share capture time and byte count — caught before any transfer.
    /// The worker's Sha256 check is the exact backstop. Only an asset that actually holds bytes can be
    /// a canonical: a Declared one may never be uploaded, and the janitor eventually expires it.</summary>
    private async Task<Guid?> FindCanonicalAsync(Guid principalId, Guid id, DeclarePhotoRequest req, CancellationToken ct)
    {
        var match = await session.Query<PhotoAsset>()
            .Where(a => a.PrincipalId == principalId
                     && a.Id != id
                     && a.TakenAt == req.TakenAt
                     && a.SizeBytes == req.SizeBytes
                     && a.ContentType == req.ContentType
                     && a.Status != AssetStatus.Duplicate
                     && a.Status != AssetStatus.Declared)
            .OrderBy(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return match?.Id;
    }

    internal static Guid AssetId(Guid principalId, string deviceId, string mediaStoreId) =>
        DeterministicGuid.From($"photo:{principalId:N}:{deviceId}:{mediaStoreId}");
}
