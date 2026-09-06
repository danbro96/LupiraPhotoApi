namespace LupiraPhotoApi.Core.Dtos.Photos;

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
}
