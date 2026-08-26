namespace LupiraPhotoApi.Core.Dtos.Photos;

/// <summary>GeoJSON-shaped map payload — feeds a MapLibre GeoJSON source directly.</summary>
public sealed class PhotoMapResponse
{
    public string Type => "FeatureCollection";
    public required List<PhotoMapFeatureDto> Features { get; set; }
}
