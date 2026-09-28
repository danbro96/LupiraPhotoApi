namespace LupiraPhotoApi.Core.Dtos.Photos;

public sealed class CameraDto
{
    public string? Make { get; set; }

    public string? Model { get; set; }

    public string? Lens { get; set; }

    public double? FocalLengthMm { get; set; }

    public double? FNumber { get; set; }

    public double? ExposureSeconds { get; set; }

    public int? Iso { get; set; }

    public string? Software { get; set; }
}
