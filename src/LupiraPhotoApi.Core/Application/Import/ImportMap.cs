namespace LupiraPhotoApi.Core.Application.Import;

/// <summary>The parsed <c>import-map.txt</c>. Keys compare case-insensitively.</summary>
public sealed class ImportMap
{
    public Dictionary<string, PersonRef> Cameras { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Keyed by a folder name or a root-relative folder path; the path wins.</summary>
    public Dictionary<string, PersonRef> People { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, PlaceRule> Places { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, AlbumRule> Albums { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> PreferFilename { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Errors { get; } = [];
}
