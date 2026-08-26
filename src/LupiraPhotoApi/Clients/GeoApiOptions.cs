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
