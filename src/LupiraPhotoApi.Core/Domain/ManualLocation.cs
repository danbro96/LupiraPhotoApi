namespace LupiraPhotoApi.Core.Domain;

/// <summary>Hand-set location corrections. A set applies at once; a clear only drops the override, and the
/// next processing run re-derives the geotag from the file, hint and history.</summary>
public static class ManualLocation
{
    /// <summary><paramref name="label"/> is the curated name to keep (null = reverse-geocode on every run);
    /// <paramref name="placeLabel"/> is what shows now — the label, else the reverse-geocoded name.</summary>
    public static void Set(PhotoAsset asset, double latitude, double longitude, string? label, string? placeLabel, DateTimeOffset now)
    {
        // A legacy phone asset holds its MediaStore fix only in Latitude/Longitude; pin it as the hint
        // before overwriting, or clearing the override would lose it.
        asset.PlaceHint ??= PlaceHint.LegacyDevice(asset);
        asset.LocationOverride = new LocationOverride { Latitude = latitude, Longitude = longitude, Label = label, SetAt = now };
        (asset.Latitude, asset.Longitude, asset.GeotagSource) = (latitude, longitude, GeotagSource.Manual);
        asset.PlaceLabel = placeLabel;
    }

    public static bool Clear(PhotoAsset asset)
    {
        if (asset.LocationOverride is null) return false;
        asset.LocationOverride = null;
        return true;
    }
}
