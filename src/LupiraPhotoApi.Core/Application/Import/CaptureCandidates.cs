namespace LupiraPhotoApi.Core.Application.Import;

/// <summary>Every date a file could be dated by, before plausibility filtering.</summary>
public sealed class CaptureCandidates
{
    public DateTime? ExifLocal { get; set; }

    public TimeSpan? ExifOffset { get; set; }

    /// <summary>The camera's clock from the import map; reads <see cref="ExifLocal"/> instead of the import zone.</summary>
    public TimeZoneInfo? CameraClock { get; set; }

    public DateTimeOffset? VideoUtc { get; set; }

    public DateTime? FilenameLocal { get; set; }

    public bool FilenameIsTransfer { get; set; }

    public DateTimeOffset? SidecarTakenUtc { get; set; }

    public DateTimeOffset? SidecarUploadUtc { get; set; }

    public DateOnly? FolderDate { get; set; }

    public FolderDatePrecision FolderPrecision { get; set; }

    public DateOnly? AlbumCoreStart { get; set; }

    public DateTimeOffset? FileTimeUtc { get; set; }
}
