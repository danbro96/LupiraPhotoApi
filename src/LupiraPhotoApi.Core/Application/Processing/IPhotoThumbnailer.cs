namespace LupiraPhotoApi.Core.Application.Processing;

/// <summary>Renders a WebP thumbnail from an image file on disk. Throws on undecodable input.</summary>
public interface IPhotoThumbnailer
{
    Task<ThumbnailResult> CreateAsync(string sourcePath, CancellationToken ct = default);
}
