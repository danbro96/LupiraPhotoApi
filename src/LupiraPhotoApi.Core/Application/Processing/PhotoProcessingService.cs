using System.Security.Cryptography;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Storage;
using Microsoft.Extensions.Logging;

namespace LupiraPhotoApi.Core.Application.Processing;

/// <summary>The per-asset pipeline: original → thumbnail → geotag. Mutates the asset in place; the
/// worker owns claiming, persistence, and retry bookkeeping. Thumbnail failure throws (retryable);
/// geotag lookups are soft — an asset becomes Ready without a label rather than being held hostage
/// to Geo/Location availability.</summary>
public sealed class PhotoProcessingService(
    IObjectStore store,
    IPhotoThumbnailer photoThumbnailer,
    IVideoThumbnailer videoThumbnailer,
    IReverseGeocoder reverseGeocoder,
    ILocationHistoryClient locationHistory,
    IMediaMetadataReader metadataReader,
    ILogger<PhotoProcessingService> logger)
{
    public async Task ProcessAsync(PhotoAsset asset, string authentikSub, CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"lupira-photo-{asset.Id:N}");
        try
        {
            // Hash rides the copy the thumbnail already needs — no client-side hashing of huge videos.
            using (var sha = SHA256.Create())
            {
                await using var target = File.Create(tempPath);
                await using var hashing = new CryptoStream(target, sha, CryptoStreamMode.Write, leaveOpen: true);
                await using (var source = await store.GetStreamAsync(asset.OriginalKey, ct))
                    await source.CopyToAsync(hashing, ct);
                await hashing.FlushFinalBlockAsync(ct);
                asset.Sha256 = Convert.ToHexStringLower(sha.Hash!);
            }

            var thumb = asset.Kind == AssetKind.Photo
                ? await photoThumbnailer.CreateAsync(tempPath, ct)
                : await videoThumbnailer.CreateAsync(tempPath, ct);

            var thumbKey = ObjectKeys.Thumb(asset.PrincipalId, asset.TakenAt, asset.Id);
            using (var thumbStream = new MemoryStream(thumb.WebpBytes))
                await store.PutAsync(thumbKey, thumbStream, thumb.WebpBytes.Length, "image/webp", ct);
            asset.ThumbKey = thumbKey;
            asset.Width ??= thumb.SourceWidth;
            asset.Height ??= thumb.SourceHeight;

            var metadata = await metadataReader.ReadAsync(tempPath, asset.Kind, ct);
            if (metadata.Camera is { } camera) asset.Camera = camera;
            await GeotagAsync(asset, metadata, authentikSub, ct);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>File EXIF GPS → the declared hint (phone coordinates or an import folder) → the owner's
    /// location history → none. A hint's curated label beats reverse geocoding.</summary>
    private async Task GeotagAsync(PhotoAsset asset, MediaMetadata metadata, string authentikSub, CancellationToken ct)
    {
        var hint = asset.PlaceHint ?? LegacyDeviceHint(asset);
        string? label = null;

        if (metadata is { Latitude: { } fileLat, Longitude: { } fileLon })
        {
            (asset.Latitude, asset.Longitude, asset.GeotagSource) = (fileLat, fileLon, GeotagSource.ExifGps);
        }
        else if (hint is { Latitude: { } hintLat, Longitude: { } hintLon })
        {
            var source = hint.Source == PlaceHintSource.Folder ? GeotagSource.Folder : GeotagSource.ExifGps;
            (asset.Latitude, asset.Longitude, asset.GeotagSource) = (hintLat, hintLon, source);
        }
        else if (await locationHistory.PlaceAtAsync(authentikSub, asset.TakenAt, ct) is { } hit)
        {
            (asset.Latitude, asset.Longitude, asset.GeotagSource) = (hit.Latitude, hit.Longitude, GeotagSource.LocationHistory);
            label = hit.Label;
            logger.LogDebug("Asset {AssetId} geotagged from location history.", asset.Id);
        }
        else
        {
            (asset.Latitude, asset.Longitude, asset.GeotagSource) = (null, null, GeotagSource.None);
        }

        if (hint?.Label is { } curated)
            label = curated;
        else if (asset.GeotagSource is GeotagSource.ExifGps or GeotagSource.Folder)
            label = await reverseGeocoder.ReverseLabelAsync(asset.Latitude!.Value, asset.Longitude!.Value, ct);
        asset.PlaceLabel = label;
    }

    /// <summary>Phone assets declared before <see cref="PhotoAsset.PlaceHint"/> existed carry their MediaStore
    /// coordinates only in Latitude/Longitude.</summary>
    private static PlaceHint? LegacyDeviceHint(PhotoAsset asset) =>
        asset is { Latitude: not null, Longitude: not null, GeotagSource: GeotagSource.None or GeotagSource.ExifGps }
            ? new PlaceHint { Source = PlaceHintSource.Device, Latitude = asset.Latitude, Longitude = asset.Longitude }
            : null;
}
