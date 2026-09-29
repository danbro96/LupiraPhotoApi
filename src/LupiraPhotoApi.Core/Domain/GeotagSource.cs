namespace LupiraPhotoApi.Core.Domain;

public enum GeotagSource
{
    None,
    ExifGps,
    LocationHistory,

    /// <summary>From an import folder's place — assumed, not measured.</summary>
    Folder,

    /// <summary>Hand-set via <see cref="PhotoAsset.LocationOverride"/>; outranks the file's own GPS.</summary>
    Manual,
}
