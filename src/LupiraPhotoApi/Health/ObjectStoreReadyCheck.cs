using LupiraPhotoApi.Core.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LupiraPhotoApi.Health;

/// <summary>Readiness against the object store — presigns are dead when it is, so failing readyz is honest.</summary>
public sealed class ObjectStoreReadyCheck(IObjectStore store) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await store.EnsureBucketAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Object store unreachable.", ex);
        }
    }
}
