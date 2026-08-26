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
    public string DeviceId { get; set; } = "";
    public string MediaStoreId { get; set; } = "";
    public AssetKind Kind { get; set; }
    public AssetStatus Status { get; set; }

    public DateTimeOffset TakenAt { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public GeotagSource GeotagSource { get; set; }
    /// <summary>Frozen reverse-geocode label; never re-resolved unless reprocessed.</summary>
    public string? PlaceLabel { get; set; }

    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? DurationSeconds { get; set; }
    public string? Sha256 { get; set; }

    public string OriginalKey { get; set; } = "";
    public string? ThumbKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UploadedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public string? LastError { get; set; }
}
