using LupiraPhotoApi.Core.Application.Import;

namespace LupiraPhotoApi.IntegrationTests;

public sealed class FakePlaceResolver : IPlaceResolver
{
    public Dictionary<string, (double Latitude, double Longitude)> Known { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<ResolvedPlace?> ResolveAsync(string query, CancellationToken ct = default) =>
        Task.FromResult(Known.TryGetValue(query, out var p)
            ? new ResolvedPlace { Latitude = p.Latitude, Longitude = p.Longitude, Via = "fake" }
            : null);
}
