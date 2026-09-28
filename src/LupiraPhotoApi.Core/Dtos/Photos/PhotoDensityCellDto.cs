namespace LupiraPhotoApi.Core.Dtos.Photos;

/// <summary>The photos in one ~100 m cell: coordinates rounded to 3 decimals.</summary>
public sealed class PhotoDensityCellDto
{
    public required double Latitude { get; set; }

    public required double Longitude { get; set; }

    public required int Count { get; set; }

    /// <summary>Distinct UTC capture dates, ascending.</summary>
    public required List<DateOnly> Days { get; set; }
}
