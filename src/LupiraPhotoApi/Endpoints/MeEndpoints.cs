using LupiraPhotoApi.Core.Dtos.Me;
using LupiraPhotoApi.Handlers;

namespace LupiraPhotoApi.Endpoints;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMe(this IEndpointRouteBuilder app)
    {
        app.MapGet("/me", (MeHandler h, CancellationToken ct) => h.GetAsync(ct))
            .RequireAuthorization("ApiPolicy").WithTags("Me").WithName("GetMe")
            .WithSummary("The caller's resolved local identity (JIT-provisioned on first login).")
            .Produces<MeDto>(StatusCodes.Status200OK);
        return app;
    }
}
