using System.Text.Json;
using LupiraPhotoApi.Core.Application.Processing;

namespace LupiraPhotoApi.Clients;

/// <summary>GeoApi <c>GET /geocode/reverse</c>. Failures return null — a geotag label is decoration,
/// never a reason to fail an asset.</summary>
public sealed class GeoReverseClient(HttpClient http, ILogger<GeoReverseClient> logger) : IReverseGeocoder
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<string?> ReverseLabelAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        try
        {
            var lat = latitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var lon = longitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var resp = await http.GetAsync($"geocode/reverse?lat={lat}&lon={lon}", ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Geo reverse returned {Status} for ({Lat}, {Lon}).", (int) resp.StatusCode, latitude, longitude);
                return null;
            }

            var body = await resp.Content.ReadFromJsonAsync<ReverseResponse>(Json, ct);
            return body is null ? null : body.Locality ?? body.Region ?? body.DisplayName;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Geo reverse failed for ({Lat}, {Lon}).", latitude, longitude);
            return null;
        }
    }

    private sealed class ReverseResponse
    {
        public string? DisplayName { get; set; }

        public string? Locality { get; set; }

        public string? Region { get; set; }
    }
}
