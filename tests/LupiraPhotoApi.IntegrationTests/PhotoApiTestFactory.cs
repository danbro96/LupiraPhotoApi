using LupiraPhotoApi.Application.Processing;
using Marten;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace LupiraPhotoApi.IntegrationTests;

/// <summary>
/// Hosts the real app against an ephemeral Postgres (Testcontainers) and the in-process
/// <see cref="FakeS3Server"/>. Runs in <c>Development</c> so the dev auth handler is wired
/// (<c>X-Dev-User</c>). The processing worker runs on a 1 s tick so declare → PUT → complete →
/// Ready is exercisable end-to-end; the geotag clients are swapped for controllable fakes and the
/// video thumbnailer for a stub (photo thumbnails run the real Magick pipeline).
/// </summary>
public sealed class PhotoApiTestFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private bool _schemaApplied;

    public FakeS3Server S3 { get; } = new();
    public FakeReverseGeocoder Geo { get; } = new();
    public FakeLocationHistory History { get; } = new();

    public PhotoApiTestFactory() => _postgres.StartAsync().GetAwaiter().GetResult();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(cfg =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                ["ObjectStorage:Endpoint"] = S3.BaseUrl,
                ["ObjectStorage:PublicEndpoint"] = S3.BaseUrl,
                ["ObjectStorage:AccessKey"] = "test",
                ["ObjectStorage:SecretKey"] = "test",
                ["Photos:ProcessingTickSeconds"] = "1",
                // Dummy issuer for the RFC 9728 metadata document; never contacted (no token is ever validated).
                ["Auth:Oidc:Authority"] = "https://auth.test/application/o/lupira-photo/",
            }));
        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IReverseGeocoder>(Geo));
            services.Replace(ServiceDescriptor.Singleton<ILocationHistoryClient>(History));
            services.Replace(ServiceDescriptor.Singleton<IVideoThumbnailer>(new StubVideoThumbnailer()));
        });
    }

    public IDocumentStore Store => Services.GetRequiredService<IDocumentStore>();

    public async Task ResetAsync()
    {
        if (!_schemaApplied)
        {
            await Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            _schemaApplied = true;
        }
        await Store.Advanced.ResetAllData();
        S3.Objects.Clear();
        Geo.Label = "Testville";
        History.Hit = null;
    }

    public HttpClient ApiClient(string email)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", email);
        return client;
    }

    /// <summary>A client with no auth header — for asserting unauthenticated requests are rejected.</summary>
    public HttpClient AnonymousClient() => CreateClient();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _postgres.DisposeAsync().AsTask().GetAwaiter().GetResult();
            S3.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}

public sealed class FakeReverseGeocoder : IReverseGeocoder
{
    public string? Label { get; set; } = "Testville";

    public Task<string?> ReverseLabelAsync(double latitude, double longitude, CancellationToken ct = default) =>
        Task.FromResult(Label);
}

public sealed class FakeLocationHistory : ILocationHistoryClient
{
    public LocationHistoryHit? Hit { get; set; }

    public Task<LocationHistoryHit?> PlaceAtAsync(string authentikSub, DateTimeOffset ts, CancellationToken ct = default) =>
        Task.FromResult(Hit);
}

/// <summary>ffmpeg stays out of CI — video posters are a fixed WebP payload.</summary>
public sealed class StubVideoThumbnailer : IVideoThumbnailer
{
    public Task<ThumbnailResult> CreateAsync(string sourcePath, CancellationToken ct = default) =>
        Task.FromResult(new ThumbnailResult { WebpBytes = [0x52, 0x49, 0x46, 0x46], SourceWidth = 1920, SourceHeight = 1080 });
}
