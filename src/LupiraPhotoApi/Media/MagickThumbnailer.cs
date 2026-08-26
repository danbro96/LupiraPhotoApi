using ImageMagick;
using LupiraPhotoApi.Core.Application.Processing;

namespace LupiraPhotoApi.Media;

/// <summary>512 px WebP thumbnails via Magick.NET (EXIF auto-orient, HEIC/AVIF included). Anything
/// ImageMagick can't decode falls through to ffmpeg, which handles stills as single-frame videos.</summary>
public sealed class MagickThumbnailer(FfmpegVideoThumbnailer ffmpegFallback) : IPhotoThumbnailer
{
    public const int MaxEdge = 512;

    public async Task<ThumbnailResult> CreateAsync(string sourcePath, CancellationToken ct = default)
    {
        try
        {
            using var image = new MagickImage(sourcePath);
            image.AutoOrient();
            var (sourceWidth, sourceHeight) = ((int)image.Width, (int)image.Height);
            // ">" = only shrink, never upscale; aspect preserved.
            image.Resize(new MagickGeometry($"{MaxEdge}x{MaxEdge}>"));
            image.Quality = 80;
            image.Format = MagickFormat.WebP;
            return new ThumbnailResult { WebpBytes = image.ToByteArray(), SourceWidth = sourceWidth, SourceHeight = sourceHeight };
        }
        catch (MagickException)
        {
            return await ffmpegFallback.CreateAsync(sourcePath, ct);
        }
    }
}
