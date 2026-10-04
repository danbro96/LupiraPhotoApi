namespace LupiraPhotoApi.Core.Dtos.Photos;

/// <summary>Hand-sets the capture time of every asset the selector matches — e.g. one camera's photos in a window
/// its clock ran wrong. Give exactly one of <see cref="ShiftBy"/> or <see cref="SetTo"/>. Needs <see cref="Ids"/>
/// or both <see cref="From"/> and <see cref="To"/>; the other criteria narrow further. Trashed assets are never
/// matched, nor are duplicates in a window.</summary>
public sealed class RetimePhotosRequest
{
    /// <summary>Added to each current time, signed (<c>-01:00:00</c>, <c>1.00:00:00</c>).</summary>
    public TimeSpan? ShiftBy { get; set; }

    /// <summary>One time for every match.</summary>
    public DateTimeOffset? SetTo { get; set; }

    public List<Guid>? Ids { get; set; }

    public DateTimeOffset? From { get; set; }

    public DateTimeOffset? To { get; set; }

    /// <summary>The EXIF camera model, case-insensitive (<c>HTC Desire</c>).</summary>
    public string? CameraModel { get; set; }

    /// <summary>The uploading device, or an import source (<c>import:handelser</c>).</summary>
    public string? DeviceId { get; set; }

    /// <summary>Report the before/after times without changing anything.</summary>
    public bool DryRun { get; set; }
}
