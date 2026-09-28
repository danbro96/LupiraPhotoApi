namespace LupiraPhotoApi.Core.Domain;

/// <summary>What the original's EXIF says about the device, read at processing. Stills only.</summary>
public sealed class CameraInfo
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
