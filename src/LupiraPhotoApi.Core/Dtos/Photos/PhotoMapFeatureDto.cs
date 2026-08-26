namespace LupiraPhotoApi.Core.Dtos.Photos;

public sealed class PhotoMapFeatureDto
{
    public string Type => "Feature";

    public required PhotoMapPointDto Geometry { get; set; }

    public required PhotoMapPropertiesDto Properties { get; set; }
}
