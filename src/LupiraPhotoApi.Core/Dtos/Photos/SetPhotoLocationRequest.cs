namespace LupiraPhotoApi.Core.Dtos.Photos;

/// <summary>A hand-set location; outranks the file's own GPS until cleared.</summary>
public sealed class SetPhotoLocationRequest
{
    public required double Latitude { get; set; }

    public required double Longitude { get; set; }

    /// <summary>A curated place name; omit to reverse-geocode the coordinates.</summary>
    public string? Label { get; set; }
}
