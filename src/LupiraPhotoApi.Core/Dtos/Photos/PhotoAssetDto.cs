using LupiraPhotoApi.Domain;

namespace LupiraPhotoApi.Dtos.Photos;

public sealed class PhotoAssetDto
{
    public required Guid Id { get; set; }
    public required AssetKind Kind { get; set; }
    public required AssetStatus Status { get; set; }
    public required DateTimeOffset TakenAt { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public required GeotagSource GeotagSource { get; set; }
    public string? PlaceLabel { get; set; }
    public required string ContentType { get; set; }
    public required long SizeBytes { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? DurationSeconds { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UploadedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? LastError { get; set; }
    /// <summary>Presigned GET, long expiry; null until processed.</summary>
    public string? ThumbUrl { get; set; }
    /// <summary>Presigned GET, short expiry; only on the single-asset endpoint.</summary>
    public string? OriginalUrl { get; set; }
}
