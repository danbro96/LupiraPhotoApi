using LupiraPhotoApi.Core.Domain;

namespace LupiraPhotoApi.Core.Dtos.Photos;

public sealed class PhotoListItemDto
{
    public required Guid Id { get; set; }

    public required AssetKind Kind { get; set; }

    public required AssetStatus Status { get; set; }

    public required DateTimeOffset TakenAt { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? PlaceLabel { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public double? DurationSeconds { get; set; }
    public required GeotagSource GeotagSource { get; set; }
    public required string ContentType { get; set; }
    public required long SizeBytes { get; set; }
    /// <summary>Only set on a Failed asset — lets the health view explain itself without a per-item fetch.</summary>
    public string? LastError { get; set; }

    /// <summary>On a Duplicate only: the asset that holds the bytes.</summary>
    public Guid? DuplicateOfId { get; set; }

    public string? ThumbUrl { get; set; }
}
