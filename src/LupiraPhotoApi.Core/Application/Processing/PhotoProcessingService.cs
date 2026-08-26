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
    ILogger<PhotoProcessingService> logger)
{
    public async Task ProcessAsync(PhotoAsset asset, string authentikSub, CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"lupira-photo-{asset.Id:N}");
        try
        {
            await using (var target = File.Create(tempPath))
            await using (var source = await store.GetStreamAsync(asset.OriginalKey, ct))
                await source.CopyToAsync(target, ct);

            var thumb = asset.Kind == AssetKind.Photo
                ? await photoThumbnailer.CreateAsync(tempPath, ct)
                : await videoThumbnailer.CreateAsync(tempPath, ct);

            var thumbKey = ObjectKeys.Thumb(asset.PrincipalId, asset.TakenAt, asset.Id);
            using (var thumbStream = new MemoryStream(thumb.WebpBytes))
                await store.PutAsync(thumbKey, thumbStream, thumb.WebpBytes.Length, "image/webp", ct);
            asset.ThumbKey = thumbKey;
            asset.Width ??= thumb.SourceWidth;
            asset.Height ??= thumb.SourceHeight;

            await GeotagAsync(asset, authentikSub, ct);
        }
        finally
        {
            try { File.Delete(tempPath); } catch (IOException) { }
        }
    }

    private async Task GeotagAsync(PhotoAsset asset, string authentikSub, CancellationToken ct)
    {
        if (asset is { Latitude: not null, Longitude: not null })
        {
            // Client-supplied EXIF GPS wins; the label is decoration on top of exact coordinates.
            asset.GeotagSource = GeotagSource.ExifGps;
            asset.PlaceLabel = await reverseGeocoder.ReverseLabelAsync(asset.Latitude.Value, asset.Longitude.Value, ct);
            return;
        }

        var hit = await locationHistory.PlaceAtAsync(authentikSub, asset.TakenAt, ct);
        if (hit is null)
        {
            asset.GeotagSource = GeotagSource.None;
            return;
        }

        asset.Latitude = hit.Latitude;
        asset.Longitude = hit.Longitude;
        asset.PlaceLabel = hit.Label;
        asset.GeotagSource = GeotagSource.LocationHistory;
        logger.LogDebug("Asset {AssetId} geotagged from location history.", asset.Id);
    }
}
