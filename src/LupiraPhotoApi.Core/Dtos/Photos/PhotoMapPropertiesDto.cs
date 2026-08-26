using LupiraPhotoApi.Core.Domain;

namespace LupiraPhotoApi.Core.Dtos.Photos;

public sealed class PhotoMapPropertiesDto
{
    public required Guid Id { get; set; }

    public required AssetKind Kind { get; set; }

    public required DateTimeOffset TakenAt { get; set; }

    public string? PlaceLabel { get; set; }

    public string? ThumbUrl { get; set; }
}
