namespace LupiraPhotoApi.Application.Processing;

public sealed class ThumbnailResult
{
    public required byte[] WebpBytes { get; set; }
    /// <summary>Dimensions of the SOURCE (not the thumb) — backfills assets the client declared without them.</summary>
    public int? SourceWidth { get; set; }
    public int? SourceHeight { get; set; }
}

/// <summary>Renders a WebP thumbnail from an image file on disk. Throws on undecodable input.</summary>
public interface IPhotoThumbnailer
{
    Task<ThumbnailResult> CreateAsync(string sourcePath, CancellationToken ct = default);
}

/// <summary>Extracts a WebP poster frame from a video file on disk. Throws on undecodable input.</summary>
public interface IVideoThumbnailer
{
    Task<ThumbnailResult> CreateAsync(string sourcePath, CancellationToken ct = default);
}
