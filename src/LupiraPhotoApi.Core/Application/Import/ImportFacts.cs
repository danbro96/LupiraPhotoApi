using LupiraPhotoApi.Core.Domain;

namespace LupiraPhotoApi.Core.Application.Import;

/// <summary>What an importer knows about a file beyond its MediaStore-style declare fields. The REST
/// surface never carries these; only the in-process importer does.</summary>
public sealed class ImportFacts
{
    public required TakenAtSource TakenAtSource { get; set; }

    public PlaceHint? PlaceHint { get; set; }

    public Guid? CapturedByContactId { get; set; }

    public CapturedBySource? CapturedBySource { get; set; }

    public string? SourceAlbum { get; set; }

    public AlbumKind? SourceAlbumKind { get; set; }

    public DateOnly? SourceAlbumDate { get; set; }

    /// <summary>A canonical the importer matched itself — a re-encoded copy the byte-level surrogate can't see.</summary>
    public Guid? DuplicateOfId { get; set; }
}
