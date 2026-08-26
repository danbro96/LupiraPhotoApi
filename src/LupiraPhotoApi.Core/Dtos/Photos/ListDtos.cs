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
    public string? ThumbUrl { get; set; }
}

public sealed class PhotoListResponse
{
    public required List<PhotoListItemDto> Items { get; set; }
    /// <summary>Opaque keyset cursor; absent on the last page.</summary>
    public string? NextCursor { get; set; }
}
