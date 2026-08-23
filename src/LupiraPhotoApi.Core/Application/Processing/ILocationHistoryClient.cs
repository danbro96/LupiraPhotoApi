namespace LupiraPhotoApi.Application.Processing;

/// <summary>A place match from the owner's location history (~100 m quantized coordinates).</summary>
public sealed class LocationHistoryHit
{
    public required double Latitude { get; set; }
    public required double Longitude { get; set; }
    public string? Label { get; set; }
}

/// <summary>Timestamp → place from LupiraLocationApi's history (the no-EXIF-GPS fallback). Null on
/// no match or upstream failure — soft, never fails an asset.</summary>
public interface ILocationHistoryClient
{
    Task<LocationHistoryHit?> PlaceAtAsync(string authentikSub, DateTimeOffset ts, CancellationToken ct = default);
}
