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

    /// <summary>Phone assets declared before <see cref="PhotoAsset.PlaceHint"/> existed carry their MediaStore
    /// coordinates only in Latitude/Longitude.</summary>
    public static PlaceHint? LegacyDevice(PhotoAsset asset) =>
        asset is { Latitude: not null, Longitude: not null, GeotagSource: GeotagSource.None or GeotagSource.ExifGps }
            ? new PlaceHint { Source = PlaceHintSource.Device, Latitude = asset.Latitude, Longitude = asset.Longitude }
            : null;
}
