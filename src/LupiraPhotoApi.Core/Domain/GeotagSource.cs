namespace LupiraPhotoApi.Core.Domain;

public enum GeotagSource
{
    None,
    ExifGps,
    LocationHistory,

    /// <summary>From an import folder's place — assumed, not measured.</summary>
    Folder,
}
