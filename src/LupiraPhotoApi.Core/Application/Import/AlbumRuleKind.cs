namespace LupiraPhotoApi.Core.Application.Import;

public enum AlbumRuleKind
{
    /// <summary>No album tag at all — the photos import as loose photos.</summary>
    None,

    /// <summary>Tag only; never offered for event linking.</summary>
    Theme,

    Event,

    /// <summary>Folded into another album (two folders holding the same event).</summary>
    SameAs,
}
