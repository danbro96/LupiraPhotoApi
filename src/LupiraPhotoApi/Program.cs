using Lupira.Auth.Jwt;
using Lupira.Clients.ServiceTokens;
using Lupira.Hosting.Defaults;
using Lupira.Hosting.Health;
using Lupira.Hosting.LanEdge;
using Lupira.Hosting.Observability;
using Lupira.Hosting.OpenApi;
using Lupira.Hosting.Problems;
using Lupira.Identity.Marten.AspNetCore;
using Lupira.Mcp;
using Lupira.Postgres.Health;
using LupiraPhotoApi.Cli;
using LupiraPhotoApi.Clients;
using LupiraPhotoApi.Core.Application;
using LupiraPhotoApi.Core.Application.Import;
using LupiraPhotoApi.Core.Application.Processing;
using LupiraPhotoApi.Core.Storage;
using LupiraPhotoApi.Endpoints;
using LupiraPhotoApi.Handlers;
using LupiraPhotoApi.Health;
using LupiraPhotoApi.Mcp;
using LupiraPhotoApi.Media;
using LupiraPhotoApi.Storage;
using LupiraPhotoApi.Workers;
using Marten;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// --- Bounded context (Marten document store on the `photo` schema + the transport-neutral services). ---
builder.Services.AddPhotoCore();
builder.Services.Configure<PhotoOptions>(builder.Configuration.GetSection(PhotoOptions.SectionName));

// --- Infrastructure adapters against the Core seams: object store, thumbnailers, geotag clients. ---
builder.Services.Configure<ObjectStorageOptions>(builder.Configuration.GetSection(ObjectStorageOptions.SectionName));
builder.Services.AddSingleton<IObjectStore, S3ObjectStore>();
builder.Services.AddSingleton<FfmpegVideoThumbnailer>();
builder.Services.AddSingleton<IVideoThumbnailer>(sp => sp.GetRequiredService<FfmpegVideoThumbnailer>());
builder.Services.AddSingleton<IPhotoThumbnailer, MagickThumbnailer>();
builder.Services.AddSingleton<IMediaMetadataReader, MediaMetadataReader>();

builder.Services.Configure<ServiceAuthOptions>(builder.Configuration.GetSection(ServiceAuthOptions.SectionName));
builder.Services.Configure<GeoApiOptions>(builder.Configuration.GetSection(GeoApiOptions.SectionName));
builder.Services.Configure<LocationApiOptions>(builder.Configuration.GetSection(LocationApiOptions.SectionName));
builder.Services.AddHttpClient<IReverseGeocoder, GeoReverseClient>((sp, http) =>
    http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<GeoApiOptions>>().Value.BaseUrl))
    .AddLupiraServiceToken(GeoHop);
builder.Services.AddHttpClient<IPlaceResolver, GeoPlaceClient>((sp, http) =>
    http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<GeoApiOptions>>().Value.BaseUrl))
    .AddLupiraServiceToken(GeoHop);
builder.Services.AddHttpClient<ILocationHistoryClient, LocationInternalClient>((sp, http) =>
    http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<LocationApiOptions>>().Value.BaseUrl))
    .AddLupiraServiceToken(sp =>
    {
        var location = sp.GetRequiredService<IOptions<LocationApiOptions>>().Value;
        return ServiceHop(sp, location.BaseUrl, location.Scope, location.DevUser, "internal:read");
    });

static OutboundHopOptions GeoHop(IServiceProvider sp)
{
    var geo = sp.GetRequiredService<IOptions<GeoApiOptions>>().Value;
    return ServiceHop(sp, geo.BaseUrl, geo.Scope, geo.DevUser);
}

static OutboundHopOptions ServiceHop(IServiceProvider sp, string baseUrl, string scope, string? devUser, string? devScopes = null)
{
    var auth = sp.GetRequiredService<IOptions<ServiceAuthOptions>>().Value;
    return new OutboundHopOptions
    {
        BaseUrl = baseUrl,
        TokenUrl = auth.TokenUrl,
        ClientId = auth.ClientId,
        ClientSecret = auth.ClientSecret,
        Scope = scope,
        DevUser = devUser,
        DevScopes = devScopes,
    };
}

// --- Host-only services: identity (claims -> Core PrincipalDirectory) + the thin REST handlers. ---
builder.Services.AddLupiraCurrentUser();
builder.Services.AddScoped<MeHandler>();
builder.Services.AddScoped<PhotosHandler>();

// MCP server for the agent (reads + location corrections), mounted at /mcp over Streamable HTTP. LAN/WireGuard-only.
builder.Services.AddLupiraMcp().WithTools<PhotoTools>();

builder.AddLupiraDefaults(o =>
{
    o.CaseInsensitiveProperties = true;
    o.ForwardedHeaders = ForwardedHeaders.None;
});

builder.Services.AddHostedService<PhotoProcessingWorker>();
builder.Services.AddHostedService<TrashPurgeWorker>();

// --- Auth: OIDC JWT (Authentik); the OIDC `sub` is the only cross-service join key. ---
builder.AddLupiraJwt();
var apiSchemes = LupiraJwtSchemes.Api(builder.Environment);
builder.Services.AddAuthorizationBuilder().AddLupiraApiPolicy(apiSchemes);

builder.AddLupiraTelemetry("lupira-photo-api");

builder.Services.AddLupiraHealth()
    .AddReadyCheck<DatabaseReadyCheck>("postgres")
    .AddReadyCheck<ObjectStoreReadyCheck>("object-store");

builder.Services.AddLupiraProblems();

builder.Services.AddOpenApi("v1", options => options.AddLupiraConventions(o =>
{
    o.Title = "Lupira Photo API";
    o.Description =
        "Family photo/video library backend for Lupira: presigned uploads, geotagging, map queries. " +
        "Authenticate with a Bearer token issued by the OIDC provider (Authentik).";
    o.DropNullEnumMembers = true;
}));

var app = builder.Build();

// One-shot schema apply (deploy step: `dotnet LupiraPhotoApi.dll --apply-schema`). Marten schema plus a
// bucket existence check so a misconfigured store fails the deploy step, not the first upload.
if (args.Contains("--apply-schema"))
{
    var store = app.Services.GetRequiredService<IDocumentStore>();
    await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    await app.Services.GetRequiredService<IObjectStore>().EnsureBucketAsync();
    Console.WriteLine("Schema applied.");
    return;
}

// Import and maintenance commands, run with `docker exec` inside the live container (see CliCommands).
if (CliCommands.Handles(args))
{
    Environment.ExitCode = await CliCommands.RunAsync(app.Services, args);
    return;
}

// LAN-only surfaces (/mcp + its discovery metadata): 404 anything arriving through the tunnel,
// before auth so a tunnelled probe never even receives a challenge.
app.UseLanOnlySurfaces("/mcp", "/.well-known/oauth-protected-resource");

app.UseLupiraDefaults();
app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapLupiraOpenApi(o => o.Title = "Lupira Photo API");

app.MapLupiraHealth();

// REST surface.
app.MapMe();
app.MapPhotos();

// Agent MCP transport (LAN/WireGuard-only; excluded from the Cloudflare Tunnel at the edge).
app.MapMcpResourceMetadata(app.Configuration["Auth:Oidc:Authority"]);
app.MapLupiraMcp();

app.Run();

// Exposes the implicit Program entry point to the integration test assembly (WebApplicationFactory<Program>).
public partial class Program;
