using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using LupiraPhotoApi.Core.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace LupiraPhotoApi.Core.Application;

/// <summary>Asset → DTO mapping with the presigned read URLs attached (thumb long-lived, original short).
/// Each URL is reused for half its lifetime: clients' URL-keyed image caches hit across refetches, and a
/// handed-out URL always has at least half its expiry left.</summary>
public sealed class PhotoPresigner(IObjectStore store, IMemoryCache cache, IOptions<PhotoOptions> options)
{
    private const string CacheKeyPrefix = "presigned-get:";

    private readonly PhotoOptions _opts = options.Value;

    public async Task<PhotoAssetDto> ToDtoAsync(PhotoAsset asset, bool includeOriginal, CancellationToken ct)
    {
        return new PhotoAssetDto
        {
            Id = asset.Id,
            Kind = asset.Kind,
            Status = asset.Status,
            TakenAt = asset.TakenAt,
            Latitude = asset.Latitude,
            Longitude = asset.Longitude,
            GeotagSource = asset.GeotagSource,
            PlaceLabel = asset.PlaceLabel,
            ContentType = asset.ContentType,
            SizeBytes = asset.SizeBytes,
            Width = asset.Width,
            Height = asset.Height,
            DurationSeconds = asset.DurationSeconds,
            CreatedAt = asset.CreatedAt,
            UploadedAt = asset.UploadedAt,
            ProcessedAt = asset.ProcessedAt,
            LastError = asset.LastError,
            DuplicateOfId = asset.DuplicateOfId,
            Camera = DtoMapping.Camera(asset.Camera),
            GpsRejection = DtoMapping.GpsRejection(asset.GpsRejection),
            TakenAtSource = asset.TakenAtSource,
            CapturedByContactId = asset.CapturedByContactId,
            CapturedBySource = asset.CapturedBySource,
            SourceAlbum = asset.SourceAlbum,
            TrashedAt = asset.TrashedAt,
            PurgesAt = AssetTrash.PurgesAt(asset, TimeSpan.FromDays(_opts.TrashRetentionDays)),
            ThumbUrl = await ThumbUrlAsync(asset, ct),
            OriginalUrl = includeOriginal && asset.Status != AssetStatus.Declared
                ? await PresignGetAsync(asset.OriginalKey, TimeSpan.FromMinutes(_opts.OriginalGetExpiryMinutes), ct)
                : null,
        };
    }

    public async Task<string?> ThumbUrlAsync(PhotoAsset asset, CancellationToken ct) =>
        asset.ThumbKey is null
            ? null
            : await PresignGetAsync(asset.ThumbKey, TimeSpan.FromHours(_opts.ThumbGetExpiryHours), ct);

    public void Forget(PhotoAsset asset)
    {
        cache.Remove(CacheKeyPrefix + asset.OriginalKey);
        if (asset.ThumbKey is { } thumbKey) cache.Remove(CacheKeyPrefix + thumbKey);
    }

    private async Task<string> PresignGetAsync(string key, TimeSpan expiry, CancellationToken ct) =>
        (await cache.GetOrCreateAsync(CacheKeyPrefix + key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = expiry / 2;
            return (await store.PresignGetAsync(key, expiry, ct)).ToString();
        }))!;
}
