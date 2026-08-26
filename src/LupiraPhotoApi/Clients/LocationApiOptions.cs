namespace LupiraPhotoApi.Clients;

/// <summary>Bound from the <c>LocationApi</c> section.</summary>
public sealed class LocationApiOptions
{
    public const string SectionName = "LocationApi";

    /// <summary>Container-to-container over medelynas_data — the /internal seam 404s through the tunnel.</summary>
    public string BaseUrl { get; set; } = "http://lupira-location-api:8080/";

    /// <summary>Needs both the audience and the internal-seam scope.</summary>
    public string Scope { get; set; } = "lupira-location-aud internal:read";

    public string? DevUser { get; set; }
}
