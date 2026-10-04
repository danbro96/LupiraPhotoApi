using LupiraPhotoApi.Core.Domain;

namespace LupiraPhotoApi.Core.Dtos.Photos;

/// <summary>A fix the GPS sweep rejected; processing skips it until restored.</summary>
public sealed class GpsRejectionDto
{
    public required double Latitude { get; set; }

    public required double Longitude { get; set; }

    public required GpsRejectionReason Reason { get; set; }
}
