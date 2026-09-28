using LupiraPhotoApi.Core.Domain;

namespace LupiraPhotoApi.Core.Application.Processing;

/// <summary>Reads capture metadata from a file on disk. Never throws on unreadable metadata — returns
/// <see cref="MediaMetadata.Empty"/> instead.</summary>
public interface IMediaMetadataReader
{
    Task<MediaMetadata> ReadAsync(string path, AssetKind kind, CancellationToken ct = default);
}
