namespace LupiraPhotoApi.Clients;

/// <summary>Bound from the <c>ServiceAuth</c> section — the lupira-photo-svc client-credentials client.</summary>
public sealed class ServiceAuthOptions
{
    public const string SectionName = "ServiceAuth";

    public string? TokenUrl { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }
}
