namespace LupiraPhotoApi.Application.Processing;

/// <summary>Coordinate → short place label (GeoApi). Null on no result or upstream failure — geotag
/// lookups are soft; they never fail an asset.</summary>
public interface IReverseGeocoder
{
    Task<string?> ReverseLabelAsync(double latitude, double longitude, CancellationToken ct = default);
}
