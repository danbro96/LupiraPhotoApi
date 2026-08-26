namespace LupiraPhotoApi.Core.Application.Processing;

/// <summary>Extracts a WebP poster frame from a video file on disk. Throws on undecodable input.</summary>
public interface IVideoThumbnailer
{
    Task<ThumbnailResult> CreateAsync(string sourcePath, CancellationToken ct = default);
}
