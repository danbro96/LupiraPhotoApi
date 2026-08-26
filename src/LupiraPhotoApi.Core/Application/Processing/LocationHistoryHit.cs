namespace LupiraPhotoApi.Core.Application.Processing;

/// <summary>A place match from the owner's location history (~100 m quantized coordinates).</summary>
public sealed class LocationHistoryHit
{
    public required double Latitude { get; set; }

    public required double Longitude { get; set; }

    public string? Label { get; set; }
}
