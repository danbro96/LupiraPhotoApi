namespace LupiraPhotoApi.Core.Domain;

/// <summary>A hand-set location. Kept apart from the resolved coordinates so every reprocess re-applies it
/// instead of letting a bad EXIF fix win again.</summary>
public sealed class LocationOverride
{
    public required double Latitude { get; set; }

    public required double Longitude { get; set; }

    /// <summary>A curated name; null means reverse-geocode the coordinates.</summary>
    public string? Label { get; set; }

    public required DateTimeOffset SetAt { get; set; }
}
