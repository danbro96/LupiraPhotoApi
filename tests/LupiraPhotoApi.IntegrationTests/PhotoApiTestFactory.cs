using Lupira.Testing.Postgres;
using LupiraPhotoApi.Core.Application.Processing;
using Marten;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LupiraPhotoApi.IntegrationTests;

/// <summary>
/// Hosts the real app against an ephemeral Postgres (Testcontainers) and the in-process
/// <see cref="FakeS3Server"/>. Runs in <c>Development</c> so the dev auth handler is wired
/// (<c>X-Dev-User</c>). The processing worker runs on a 1 s tick so declare → PUT → complete →
/// Ready is exercisable end-to-end; the geotag clients are swapped for controllable fakes and the
/// video thumbnailer for a stub (photo thumbnails run the real Magick pipeline).
/// </summary>
public sealed class PhotoApiTestFactory : LupiraApiFactory<Program>
{
    public FakeS3Server S3 { get; } = new();

    public FakeReverseGeocoder Geo { get; } = new();

    public FakeLocationHistory History { get; } = new();

    public FakePlaceResolver Places { get; } = new();

    public IDocumentStore Store => Services.GetRequiredService<IDocumentStore>();

    protected override string AuthentikSlug => "lupira-photo";

    protected override void AddSettings(IDictionary<string, string?> settings)
    {
        settings["ObjectStorage:Endpoint"] = S3.BaseUrl;
        settings["ObjectStorage:PublicEndpoint"] = S3.BaseUrl;
        settings["ObjectStorage:AccessKey"] = "test";
        settings["ObjectStorage:SecretKey"] = "test";
        settings["Photos:ProcessingTickSeconds"] = "1";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IReverseGeocoder>(Geo));
            services.Replace(ServiceDescriptor.Singleton<ILocationHistoryClient>(History));
            services.Replace(ServiceDescriptor.Singleton<IVideoThumbnailer>(new StubVideoThumbnailer()));
            services.RemoveAll<LupiraPhotoApi.Core.Application.Import.IPlaceResolver>();
            services.AddSingleton<LupiraPhotoApi.Core.Application.Import.IPlaceResolver>(Places);
        });
    }

    protected override Task ApplySchemaAsync() => Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

    protected override async Task ResetDataAsync()
    {
        await Store.Advanced.ResetAllData();
        S3.Objects.Clear();
        Geo.Label = "Testville";
        History.Hit = null;
        History.HitAt = null;
        Places.Known.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            S3.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
