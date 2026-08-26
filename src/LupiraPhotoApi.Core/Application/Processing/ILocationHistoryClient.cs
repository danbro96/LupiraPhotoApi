namespace LupiraPhotoApi.Core.Application.Processing;

/// <summary>Timestamp → place from LupiraLocationApi's history (the no-EXIF-GPS fallback). Null on
/// no match or upstream failure — soft, never fails an asset.</summary>
public interface ILocationHistoryClient
{
    Task<LocationHistoryHit?> PlaceAtAsync(string authentikSub, DateTimeOffset ts, CancellationToken ct = default);
}
