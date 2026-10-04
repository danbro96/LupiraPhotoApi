namespace LupiraPhotoApi.Core.Dtos.Photos;

/// <summary>Finds impossible GPS fixes among Ready photos with exact capture times, per camera. Spikes are
/// rejected on apply; repeated coordinates only when listed in <see cref="RejectCoordinates"/>.</summary>
public sealed class GpsSweepRequest
{
    public DateTimeOffset? From { get; set; }

    public DateTimeOffset? To { get; set; }

    /// <summary>Reject and re-queue; without it the sweep only reports.</summary>
    public bool Apply { get; set; }

    /// <summary>Repeated coordinates to reject too (within ~1 m), as reported under repeats.</summary>
    public List<GpsCoordinateDto>? RejectCoordinates { get; set; }
}
