using LupiraPhotoApi.Core.Domain;

namespace LupiraPhotoApi.Core.Application.Processing;

/// <summary>What a file says about itself. Every field is optional — absent, not an error.</summary>
public sealed class MediaMetadata
{
    public static readonly MediaMetadata Empty = new();

    /// <summary>Wall-clock capture time as written (EXIF carries no zone unless <see cref="TakenAtOffset"/> is set).</summary>
    public DateTime? TakenAtLocal { get; set; }

    public TimeSpan? TakenAtOffset { get; set; }

    /// <summary>A container timestamp already in UTC (video <c>creation_time</c>).</summary>
    public DateTimeOffset? TakenAtUtc { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public CameraInfo? Camera { get; set; }
}
