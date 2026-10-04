using System.Globalization;
using System.Text.Json;
using Lupira.Auth.DevUser;
using Lupira.Hosting.Defaults;
using Lupira.Hosting.Health;
using Lupira.Hosting.LanEdge;
using Lupira.Hosting.Observability;
using Lupira.Hosting.Problems;
using Lupira.Mcp;
using LupiraPhotoApi.Auth;
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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

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
builder.Services.AddHttpClient(nameof(ServiceTokenProvider));
builder.Services.AddSingleton<ServiceTokenProvider>();
builder.Services.AddHttpClient<IReverseGeocoder, GeoReverseClient>((sp, http) =>
    http.BaseAddress = new Uri(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GeoApiOptions>>().Value.BaseUrl));
builder.Services.AddHttpClient<IPlaceResolver, GeoPlaceClient>((sp, http) =>
    http.BaseAddress = new Uri(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GeoApiOptions>>().Value.BaseUrl));
builder.Services.AddHttpClient<ILocationHistoryClient, LocationInternalClient>((sp, http) =>
    http.BaseAddress = new Uri(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocationApiOptions>>().Value.BaseUrl));

// --- Host-only services: identity (claims -> Core PrincipalDirectory) + the thin REST handlers. ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
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
var isOpenApiBuild = Environment.GetCommandLineArgs()
    .Any(a => a.Contains("getdocument", StringComparison.OrdinalIgnoreCase));

var oidc = builder.Configuration.GetSection(OidcAuthOptions.SectionName).Get<OidcAuthOptions>() ?? new OidcAuthOptions();
if (!isOpenApiBuild && !builder.Environment.IsDevelopment()
    && (string.IsNullOrWhiteSpace(oidc.Authority) || string.IsNullOrWhiteSpace(oidc.Audience)))
    throw new InvalidOperationException("Auth:Oidc Authority + Audience are required outside Development.");

var authBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = oidc.Authority;
        options.Audience = oidc.Audience;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.Events = new JwtBearerEvents
        {
            // MCP auth spec: a 401 on /mcp advertises the RFC 9728 metadata so clients can discover the
            // issuer. HandleResponse suppresses the default bare "Bearer" header so exactly one goes out.
            OnChallenge = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/mcp"))
                {
                    ctx.HandleResponse();
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    ctx.Response.Headers.WWWAuthenticate =
                        $"Bearer resource_metadata=\"{McpResourceMetadata.ResourceMetadataUrl(ctx.Request)}\"";
                }

                return Task.CompletedTask;
            },
        };
    });

// Development-only: allow X-Dev-User header auth so the API can be exercised without Authentik.
if (builder.Environment.IsDevelopment())
    authBuilder.AddLupiraDevHeaderAuth();

string[] apiSchemes = builder.Environment.IsDevelopment()
    ? [JwtBearerDefaults.AuthenticationScheme, DevAuthenticationBuilderExtensions.DefaultScheme]
    : [JwtBearerDefaults.AuthenticationScheme];

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ApiPolicy", p => p.AddAuthenticationSchemes(apiSchemes).RequireAuthenticatedUser());

builder.AddLupiraTelemetry("lupira-photo-api");

builder.Services.AddLupiraHealth()
    .AddReadyCheck<DatabaseReadyCheck>("postgres")
    .AddReadyCheck<ObjectStoreReadyCheck>("object-store");

builder.Services.AddLupiraProblems();

builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, _) =>
    {
        document.Info = new()
        {
            Title = "Lupira Photo API",
            Version = "v1",
            Description =
                "Family photo/video library backend for Lupira: presigned uploads, geotagging, map queries. " +
                "Authenticate with a Bearer token issued by the OIDC provider (Authentik).",
        };
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "OIDC bearer token. Send as `Authorization: Bearer <token>`.",
        };
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        document.Components.Schemas["ProblemDetails"] = ProblemDetailsSchema();
        return Task.CompletedTask;
    });
    // A nullable use of an enum (ItemStatus?) makes the framework append null to the shared
    // component schema, although the property's own oneOf already carries the nullability.
    // Generators read that null onto the enum type itself, so non-nullable uses inherit it too.
    options.AddSchemaTransformer((schema, context, _) =>
    {
        if (schema.Enum is { Count: > 0 } members)
        {
            for (var i = members.Count - 1; i >= 0; i--)
            {
                if (members[i] is null || members[i]!.GetValueKind() == JsonValueKind.Null)
                {
                    members.RemoveAt(i);
                }
            }
        }

        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, _) =>
    {
        var endpointMetadata = context.Description.ActionDescriptor.EndpointMetadata;
        var requiresAuth = endpointMetadata.OfType<IAuthorizeData>().Any()
                        && !endpointMetadata.OfType<IAllowAnonymous>().Any();
        if (requiresAuth)
        {
            operation.Security ??= new List<OpenApiSecurityRequirement>();
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = new List<string>(),
            });
            AddProblem(operation, context.Document, StatusCodes.Status401Unauthorized, "Unauthorized");
        }

        // The cross-cutting code no endpoint declares — ProblemExceptionHandler produces it.
        AddProblem(operation, context.Document, StatusCodes.Status500InternalServerError, "Internal server error");

        // Bodyless 4xx/5xx come from the non-generic arms of the typed-result unions (NotFound,
        // UnauthorizedHttpResult). UseStatusCodePages fills them at runtime, so declare the shape.
        foreach (var code in operation.Responses?.Keys.ToList() ?? [])
        {
            if (code.Length != 3 || code[0] is not ('4' or '5')) continue;
            var existing = operation.Responses![code];
            if (existing.Content is { Count: > 0 }) continue;
            operation.Responses[code] = new OpenApiResponse
            {
                Description = existing.Description,
                Content = ProblemContent(context.Document),
            };
        }

        return Task.CompletedTask;
    });
});

// Every error response carries the same shape, so a generated client types its error once instead of
// falling back to `void`.
static Dictionary<string, OpenApiMediaType> ProblemContent(OpenApiDocument document) =>
    new() { ["application/problem+json"] = new() { Schema = new OpenApiSchemaReference("ProblemDetails", document) } };

static void AddProblem(OpenApiOperation operation, OpenApiDocument document, int status, string description)
{
    var code = status.ToString(CultureInfo.InvariantCulture);
    operation.Responses ??= [];
    if (operation.Responses.ContainsKey(code)) return;
    operation.Responses[code] = new OpenApiResponse { Description = description, Content = ProblemContent(document) };
}

// RFC 9457. Declared here because nothing in this API returns the CLR type directly, so the generator
// never emits it.
static OpenApiSchema ProblemDetailsSchema() => new()
{
    Type = JsonSchemaType.Object,
    Description = "RFC 9457 problem details.",
    Properties = new Dictionary<string, IOpenApiSchema>
    {
        ["type"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["title"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer | JsonSchemaType.Null, Format = "int32" },
        ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["instance"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
    },
};

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

app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
app.MapScalarApiReference("/scalar", o => o
        .WithTitle("Lupira Photo API")
        .WithTheme(ScalarTheme.BluePlanet))
    .AllowAnonymous();

app.MapGet("/", () => TypedResults.Redirect("/scalar"))
   .ExcludeFromDescription()
   .AllowAnonymous();

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
