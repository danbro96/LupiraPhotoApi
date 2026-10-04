namespace LupiraPhotoApi.Core.Dtos.Photos;

/// <summary>One coordinate one camera reported on several days.</summary>
public sealed class GpsRepeatDto
{
    public required double Latitude { get; set; }

    public required double Longitude { get; set; }

    /// <summary>The phone's device id, else the EXIF camera name.</summary>
    public required string Camera { get; set; }

    public required int Count { get; set; }

    /// <summary>Distinct UTC days.</summary>
    public required int Days { get; set; }

    public required DateTimeOffset First { get; set; }

    public required DateTimeOffset Last { get; set; }
}
