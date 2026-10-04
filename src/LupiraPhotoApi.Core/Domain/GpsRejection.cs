namespace LupiraPhotoApi.Core.Domain;

/// <summary>A GPS fix judged impossible. Processing skips a file or phone fix at this coordinate, so the photo
/// falls through to location history, then to none, on every reprocess and re-import.</summary>
public sealed class GpsRejection
{
    public required double Latitude { get; set; }

    public required double Longitude { get; set; }

    public required GpsRejectionReason Reason { get; set; }

    public required DateTimeOffset At { get; set; }
}
