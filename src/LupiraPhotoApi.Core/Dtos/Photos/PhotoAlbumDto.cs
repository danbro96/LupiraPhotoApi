using LupiraPhotoApi.Core.Domain;

namespace LupiraPhotoApi.Core.Dtos.Photos;

/// <summary>One imported event folder or album, summarised for linking to a calendar event.</summary>
public sealed class PhotoAlbumDto
{
    public required string Name { get; set; }

    public required AlbumKind Kind { get; set; }

    /// <summary>The import source (<c>handelser</c>, <c>takeout</c>, …); null for phone assets.</summary>
    public string? Source { get; set; }

    /// <summary>The date the folder name carries, when it has one.</summary>
    public DateOnly? FolderDate { get; set; }

    public required int Count { get; set; }

    /// <summary>The densest run of exactly-dated capture days; null when no photo has an exact date.</summary>
    public DateOnly? CoreFrom { get; set; }

    public DateOnly? CoreTo { get; set; }

    /// <summary>Photos outside the core run, or dated only approximately.</summary>
    public required int Outliers { get; set; }

    public double? CentroidLatitude { get; set; }

    public double? CentroidLongitude { get; set; }
}
