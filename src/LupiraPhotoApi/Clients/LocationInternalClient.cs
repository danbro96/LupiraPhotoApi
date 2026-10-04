using System.Text.Json;
using LupiraPhotoApi.Core.Application.Processing;

namespace LupiraPhotoApi.Clients;

/// <summary>LupiraLocationApi <c>GET /internal/location/place-at</c> — the no-EXIF-GPS fallback.
/// Returns ~100 m quantized coordinates (the API's synergy-safe cap). Null on no match or failure.</summary>
public sealed class LocationInternalClient(HttpClient http, ILogger<LocationInternalClient> logger) : ILocationHistoryClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<LocationHistoryHit?> PlaceAtAsync(string authentikSub, DateTimeOffset ts, CancellationToken ct = default)
    {
        try
        {
            var query = $"internal/location/place-at?sub={Uri.EscapeDataString(authentikSub)}&ts={Uri.EscapeDataString(ts.UtcDateTime.ToString("O"))}";
            using var resp = await http.GetAsync(query, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Location place-at returned {Status} for ts {Ts}.", (int) resp.StatusCode, ts);
                return null;
            }

            var body = await resp.Content.ReadFromJsonAsync<PlaceAtResponse>(Json, ct);
            if (body is null || body.Source == "none" || body.Lat is null || body.Lon is null) return null;
            return new LocationHistoryHit { Latitude = body.Lat.Value, Longitude = body.Lon.Value, Label = body.Label };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Location place-at failed for ts {Ts}.", ts);
            return null;
        }
    }

    private sealed class PlaceAtResponse
    {
        public string? Label { get; set; }

        public double? Lat { get; set; }

        public double? Lon { get; set; }

        public string? Source { get; set; }
    }
}
