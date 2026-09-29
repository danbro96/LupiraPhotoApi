using LupiraPhotoApi.Core.Domain;

namespace LupiraPhotoApi.Core.Dtos.Photos;

/// <summary>One photo when the count is 1, else a grid cell drawn as a count bubble.</summary>
public sealed class PhotoMapPropertiesDto
{
    public required int Count { get; set; }

    public Guid? Id { get; set; }

    public AssetKind? Kind { get; set; }

    public DateTimeOffset? TakenAt { get; set; }

    public string? PlaceLabel { get; set; }

    /// <summary>The photo's, or a cell's newest photo's.</summary>
    public string? ThumbUrl { get; set; }

    /// <summary>A cell's photo extent, [minLon, minLat, maxLon, maxLat] — where tapping it zooms to.</summary>
    public double[]? Bounds { get; set; }
}
