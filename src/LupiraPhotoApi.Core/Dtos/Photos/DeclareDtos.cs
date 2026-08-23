using LupiraPhotoApi.Domain;

namespace LupiraPhotoApi.Dtos.Photos;

/// <summary>Client-side MediaStore metadata for one asset. (DeviceId, MediaStoreId) under the caller is
/// the idempotency key — retrying a declare returns the same asset.</summary>
public sealed class DeclarePhotoRequest
{
    public required string DeviceId { get; set; }
    public required string MediaStoreId { get; set; }
    public required string ContentType { get; set; }
    public required long SizeBytes { get; set; }
    public required DateTimeOffset TakenAt { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? DurationSeconds { get; set; }
    public string? Sha256 { get; set; }
}

/// <summary>Declare outcome. UploadUrl is present only while the asset still needs bytes (status
/// Declared); an already-uploaded asset returns its status so the client skips the transfer.</summary>
public sealed class DeclaredPhotoResponse
{
    public required Guid AssetId { get; set; }
    public required AssetStatus Status { get; set; }
    public string? UploadUrl { get; set; }
    public DateTimeOffset? UploadExpiresAt { get; set; }
    /// <summary>Headers the PUT must echo — they are part of the presigned signature.</summary>
    public required Dictionary<string, string> RequiredHeaders { get; set; }
}
