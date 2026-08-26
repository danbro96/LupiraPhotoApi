using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using LupiraPhotoApi.Core.Storage;
using Microsoft.Extensions.Options;

namespace LupiraPhotoApi.Core.Application;

/// <summary>Asset → DTO mapping with the presigned read URLs attached (thumb long-lived, original short).</summary>
public sealed class PhotoPresigner(IObjectStore store, IOptions<PhotoOptions> options)
{
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
            ThumbUrl = await ThumbUrlAsync(asset, ct),
            OriginalUrl = includeOriginal && asset.Status != AssetStatus.Declared
                ? (await store.PresignGetAsync(asset.OriginalKey, TimeSpan.FromMinutes(_opts.OriginalGetExpiryMinutes), ct)).ToString()
                : null,
        };
    }

    public async Task<string?> ThumbUrlAsync(PhotoAsset asset, CancellationToken ct) =>
        asset.ThumbKey is null
            ? null
            : (await store.PresignGetAsync(asset.ThumbKey, TimeSpan.FromHours(_opts.ThumbGetExpiryHours), ct)).ToString();
}
