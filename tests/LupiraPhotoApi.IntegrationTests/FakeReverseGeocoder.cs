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

public sealed class FakeReverseGeocoder : IReverseGeocoder
{
    public string? Label { get; set; } = "Testville";

    public Task<string?> ReverseLabelAsync(double latitude, double longitude, CancellationToken ct = default) =>
        Task.FromResult(Label);
}
