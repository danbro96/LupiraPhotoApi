namespace LupiraPhotoApi.Core.Dtos.Photos;

public sealed class GpsSpikeDto
{
    public required Guid Id { get; set; }

    public required DateTimeOffset TakenAt { get; set; }

    public required double Latitude { get; set; }

    public required double Longitude { get; set; }

    /// <summary>The phone's device id, else the EXIF camera name.</summary>
    public required string Camera { get; set; }

    public required double SpeedInKmh { get; set; }

    public required double SpeedOutKmh { get; set; }
}
