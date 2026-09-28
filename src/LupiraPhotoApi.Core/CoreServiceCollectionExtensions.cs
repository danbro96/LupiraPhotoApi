using LupiraPhotoApi.Core.Application;
using LupiraPhotoApi.Core.Application.Processing;
using LupiraPhotoApi.Core.Data;
using Marten;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the LupiraPhotoApi bounded context: the Marten store (documents on the <c>photo</c>
/// schema) and the transport-neutral services. The object store, thumbnailers, and geotag clients are
/// host concerns registered against the Core seams.</summary>
public static class CoreServiceCollectionExtensions
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=lupira_photo;Username=lupira_photo_user;Password=devpassword";

    public static IServiceCollection AddPhotoCore(this IServiceCollection services)
    {
        // Resolve the connection string lazily from IConfiguration so test hosts (WebApplicationFactory) can
        // override ConnectionStrings:Postgres before the store is built.
        services.AddMarten(sp =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres") ?? DefaultConnectionString;
            var opts = new StoreOptions();
            opts.Connection(connectionString);
            opts.UseLupiraPhoto();
            return opts;
        }).UseLightweightSessions();

        services.AddMemoryCache();
        services.AddScoped<PrincipalDirectory>();
        services.AddScoped<PhotoDeclareService>();
        services.AddScoped<PhotoCompleteService>();
        services.AddScoped<PhotoQueryService>();
        services.AddScoped<PhotoDeleteService>();
        services.AddScoped<PhotoTrashService>();
        services.AddScoped<PhotoStatsService>();
        services.AddScoped<PhotoCurationService>();
        services.AddScoped<PhotoAlbumService>();
        services.AddScoped<PhotoMaintenanceService>();
        services.AddScoped<LupiraPhotoApi.Core.Application.Import.PhotoImporter>();
        services.AddScoped<PhotoPresigner>();
        services.AddScoped<PhotoProcessingService>();
        return services;
    }
}
