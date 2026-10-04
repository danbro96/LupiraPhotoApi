using LupiraPhotoApi.Core.Application.Processing;
using Marten;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace LupiraPhotoApi.IntegrationTests;

public sealed class FakeLocationHistory : ILocationHistoryClient
{
    public LocationHistoryHit? Hit { get; set; }

    /// <summary>Wins over <see cref="Hit"/> when set.</summary>
    public Func<DateTimeOffset, LocationHistoryHit?>? HitAt { get; set; }

    public Task<LocationHistoryHit?> PlaceAtAsync(string authentikSub, DateTimeOffset ts, CancellationToken ct = default) =>
        Task.FromResult(HitAt is { } at ? at(ts) : Hit);
}
