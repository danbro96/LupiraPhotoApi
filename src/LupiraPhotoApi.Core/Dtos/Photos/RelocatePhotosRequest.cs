namespace LupiraPhotoApi.Core.Dtos.Photos;

/// <summary>Hand-sets one location on every asset the selector matches — e.g. all of one camera's photos in a
/// window that carry the same stale GPS fix. Needs <see cref="Ids"/> or both <see cref="From"/> and
/// <see cref="To"/>; the other criteria narrow further. Duplicates and trashed assets are never matched.</summary>
public sealed class RelocatePhotosRequest
{
    public required double Latitude { get; set; }

    public required double Longitude { get; set; }

    /// <summary>A curated place name; omit to reverse-geocode the coordinates.</summary>
    public string? Label { get; set; }

    public List<Guid>? Ids { get; set; }

    public DateTimeOffset? From { get; set; }

    public DateTimeOffset? To { get; set; }

    /// <summary>The EXIF camera model, case-insensitive (<c>GT-I9300</c>).</summary>
    public string? CameraModel { get; set; }

    /// <summary>Only assets currently at this coordinate (within ~1 m); pairs with <see cref="AtLongitude"/>.</summary>
    public double? AtLatitude { get; set; }

    public double? AtLongitude { get; set; }

    /// <summary>Report what would move without changing anything.</summary>
    public bool DryRun { get; set; }
}
