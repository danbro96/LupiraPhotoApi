namespace LupiraPhotoApi.Core.Application;

public sealed class PhotoStats
{
    public required long TotalAssets { get; set; }

    public required long TotalBytes { get; set; }

    public required Dictionary<string, int> ByKind { get; set; }

    public required Dictionary<string, int> ByStatus { get; set; }

    public required Dictionary<string, int> ByGeotagSource { get; set; }

    public required Dictionary<string, int> ByMonth { get; set; }

    /// <summary>"Sony G8341" → count; what the importer's camera map is built from.</summary>
    public required Dictionary<string, int> ByCamera { get; set; }
}
