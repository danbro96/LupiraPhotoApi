using System.Text.Json;
using LupiraPhotoApi.Application.Processing;
using Microsoft.Extensions.Options;

namespace LupiraPhotoApi.Clients;

/// <summary>Bound from the <c>Geo</c> section.</summary>
public sealed class GeoApiOptions
{
    public const string SectionName = "Geo";

    /// <summary>Container-to-container over medelynas_data — never via the tunnel.</summary>
    public string BaseUrl { get; set; } = "http://lupira-geo-api:8080/";

    public string Scope { get; set; } = "lupira-geo-aud";

    /// <summary>Development fallback: sent as <c>X-Dev-User</c> when no service credentials are configured.</summary>
    public string? DevUser { get; set; }
}

/// <summary>GeoApi <c>GET /geocode/reverse</c>. Failures return null — a geotag label is decoration,
/// never a reason to fail an asset.</summary>
public sealed class GeoReverseClient(
    HttpClient http,
    ServiceTokenProvider tokens,
    IOptions<GeoApiOptions> options,
    ILogger<GeoReverseClient> logger) : IReverseGeocoder
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly GeoApiOptions _opts = options.Value;

    public async Task<string?> ReverseLabelAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        try
        {
            var lat = latitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var lon = longitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"geocode/reverse?lat={lat}&lon={lon}");
            if (tokens.IsConfigured)
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {await tokens.GetTokenAsync(_opts.Scope, ct)}");
            else if (!string.IsNullOrWhiteSpace(_opts.DevUser))
                req.Headers.TryAddWithoutValidation("X-Dev-User", _opts.DevUser);

            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Geo reverse returned {Status} for ({Lat}, {Lon}).", (int)resp.StatusCode, latitude, longitude);
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
