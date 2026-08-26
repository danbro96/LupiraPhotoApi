namespace LupiraPhotoApi.Core.Dtos.Photos;

public sealed class PhotoMapPointDto
{
    public string Type => "Point";
    /// <summary>[lon, lat].</summary>
    public required double[] Coordinates { get; set; }
}
