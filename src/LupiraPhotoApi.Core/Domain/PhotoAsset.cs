namespace LupiraPhotoApi.Core.Domain;

/// <summary>
/// One camera-roll asset (photo or video). The identity is deterministic over
/// (principal, device, MediaStore id) — a retried declare upserts the same document, which is the
/// entire idempotency story. Bytes live in the object store under <see cref="OriginalKey"/>;
/// this document is the queryable metadata plus the processing-queue bookkeeping.
/// </summary>
public sealed class PhotoAsset
{
    public Guid Id { get; set; }

    public Guid PrincipalId { get; set; }

    public string DeviceId { get; set; } = string.Empty;

    public string MediaStoreId { get; set; } = string.Empty;

    public AssetKind Kind { get; set; }

    public AssetStatus Status { get; set; }

    public DateTimeOffset TakenAt { get; set; }

    public TakenAtSource TakenAtSource { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public GeotagSource GeotagSource { get; set; }

    /// <summary>The place hint's curated label, else a reverse-geocode label; re-derived only on reprocess.</summary>
    public string? PlaceLabel { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public double? DurationSeconds { get; set; }

    public CameraInfo? Camera { get; set; }

    public PlaceHint? PlaceHint { get; set; }

    public Guid? CapturedByContactId { get; set; }

    public CapturedBySource? CapturedBySource { get; set; }

    /// <summary>The event folder or album an import came from.</summary>
    public string? SourceAlbum { get; set; }

    public AlbumKind? SourceAlbumKind { get; set; }

    /// <summary>The date an event folder's name carries (<c>2017-10-03 Svalbard</c>).</summary>
    public DateOnly? SourceAlbumDate { get; set; }

    /// <summary>Of the original bytes, computed by the worker — never trusted from the client.</summary>
    public string? Sha256 { get; set; }

    /// <summary>On a Duplicate only: the asset that holds the bytes.</summary>
    public Guid? DuplicateOfId { get; set; }

    /// <summary>Soft delete, orthogonal to <see cref="Status"/>; the bytes stay until a purge.</summary>
    public DateTimeOffset? TrashedAt { get; set; }

    public string OriginalKey { get; set; } = string.Empty;

    public string? ThumbKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UploadedAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public DateTimeOffset? LeaseUntil { get; set; }

    public string? LastError { get; set; }
}
