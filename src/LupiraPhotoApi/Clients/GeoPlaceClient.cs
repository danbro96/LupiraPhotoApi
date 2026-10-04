using System.Text.Json;
using LupiraPhotoApi.Core.Application.Import;

namespace LupiraPhotoApi.Clients;

/// <summary>GeoApi gazetteer (<c>GET /places?q=</c>) first — contact addresses and saved places already live
/// there — then the geocoder (<c>GET /geocode/forward</c>). Anything ambiguous is null, never a guess.</summary>
public sealed class GeoPlaceClient(HttpClient http, ILogger<GeoPlaceClient> logger) : IPlaceResolver
{
    private const double SameSpotMeters = 1000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ResolvedPlace?> ResolveAsync(string query, CancellationToken ct = default)
    {
        var q = Uri.EscapeDataString(query);
        var places = await GetAsync<List<GazetteerPlace>>($"places?q={q}&hasCoordinates=true&limit=5", ct) ?? [];
        var located = places.Where(p => p.Latitude is not null && p.Longitude is not null).ToList();
        var exact = located.Where(p => string.Equals(p.Name, query, StringComparison.OrdinalIgnoreCase)).ToList();
        var pick = exact.Count == 1 ? exact[0] : located.Count == 1 ? located[0] : null;
        if (pick is not null)
            return new ResolvedPlace { Latitude = pick.Latitude!.Value, Longitude = pick.Longitude!.Value, Via = $"gazetteer \"{pick.Name}\"" };

        var hits = await GetAsync<List<GeocodeHit>>($"geocode/forward?q={q}&limit=3", ct) ?? [];
        if (hits.Count == 0) return null;
        var first = hits[0];
        if (hits.Count > 1 && GeoMath.DistanceMeters(first.Latitude, first.Longitude, hits[1].Latitude, hits[1].Longitude) > SameSpotMeters)
            return null;
        return new ResolvedPlace { Latitude = first.Latitude, Longitude = first.Longitude, Via = $"geocoder \"{first.DisplayName}\"" };
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync(path, ct);
            if (resp.IsSuccessStatusCode) return await resp.Content.ReadFromJsonAsync<T>(Json, ct);
            logger.LogWarning("Geo {Path} returned {Status}.", path, (int) resp.StatusCode);
            return default;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Geo {Path} failed.", path);
            return default;
        }
    }

    private sealed class GazetteerPlace
    {
        public string? Name { get; set; }

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }
    }

    private sealed class GeocodeHit
    {
        public string? DisplayName { get; set; }

        public double Latitude { get; set; }

        public double Longitude { get; set; }
    }
}
