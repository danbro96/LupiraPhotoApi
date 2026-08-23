using System.Diagnostics;

namespace LupiraPhotoApi.Domain;

/// <summary>Domain-specific tracing source, registered with OpenTelemetry in Program.cs.</summary>
public static class PhotoTelemetry
{
    public const string ActivitySourceName = "LupiraPhotoApi.Photos";
    public static readonly ActivitySource Source = new(ActivitySourceName);
}
