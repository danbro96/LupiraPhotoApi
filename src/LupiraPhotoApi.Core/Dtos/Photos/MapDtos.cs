using LupiraPhotoApi.Domain;

namespace LupiraPhotoApi.Dtos.Photos;

/// <summary>GeoJSON-shaped map payload — feeds a MapLibre GeoJSON source directly.</summary>
public sealed class PhotoMapResponse
{
    public string Type => "FeatureCollection";
    public required List<PhotoMapFeatureDto> Features { get; set; }
}

public sealed class PhotoMapFeatureDto
{
    public string Type => "Feature";
    public required PhotoMapPointDto Geometry { get; set; }
    public required PhotoMapPropertiesDto Properties { get; set; }
}

public sealed class PhotoMapPointDto
{
    public string Type => "Point";
    /// <summary>[lon, lat].</summary>
    public required double[] Coordinates { get; set; }
}

public sealed class PhotoMapPropertiesDto
{
    public required Guid Id { get; set; }
    public required AssetKind Kind { get; set; }
    public required DateTimeOffset TakenAt { get; set; }
    public string? PlaceLabel { get; set; }
    public string? ThumbUrl { get; set; }
}
