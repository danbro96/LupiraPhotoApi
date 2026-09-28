using LupiraPhotoApi.Core.Domain;

namespace LupiraPhotoApi.Core.Application.Import;

/// <summary>A stored import asset a new file may be a re-encoded copy of.</summary>
public sealed class NearCopy
{
    /// <summary>The asset holding the bytes — the stored copy itself, or what it is a duplicate of.</summary>
    public required Guid CanonicalId { get; set; }

    public required AssetKind Kind { get; set; }

    public required string Name { get; set; }

    public required DateTimeOffset TakenAt { get; set; }

    public required long SizeBytes { get; set; }

    public string? SourceAlbum { get; set; }

    public AlbumKind? SourceAlbumKind { get; set; }

    public DateOnly? SourceAlbumDate { get; set; }
}
