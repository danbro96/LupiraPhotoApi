namespace LupiraPhotoApi.Core.Application.Import;

public sealed class AlbumRule
{
    public required AlbumRuleKind Kind { get; set; }

    public string? SameAs { get; set; }
}
