namespace LupiraPhotoApi.Core.Domain;

/// <summary>Location supplied at declare — the phone's MediaStore coordinates, or an import folder's place.
/// Kept apart from the resolved <see cref="PhotoAsset.Latitude"/>/<see cref="PhotoAsset.Longitude"/> so
/// processing can re-derive the geotag on every run.</summary>
public sealed class PlaceHint
{
    public required PlaceHintSource Source { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    /// <summary>A curated name (folder or address); wins over reverse geocoding.</summary>
    public string? Label { get; set; }
}
