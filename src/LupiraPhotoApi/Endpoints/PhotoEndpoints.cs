using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using LupiraPhotoApi.Handlers;

namespace LupiraPhotoApi.Endpoints;

/// <summary>The owner-facing asset surface (OIDC-authed). Bytes move via presigned URLs against the
/// object store's public host — this API only orchestrates.</summary>
public static class PhotoEndpoints
{
    public static IEndpointRouteBuilder MapPhotos(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/photos").RequireAuthorization("ApiPolicy").WithTags("Photos");

        g.MapPost("/", (DeclarePhotoRequest body, PhotosHandler h, CancellationToken ct) => h.DeclareAsync(body, ct))
            .WithSummary("Declare an asset (idempotent) and receive a presigned upload URL while bytes are pending.")
            .Produces<DeclaredPhotoResponse>(StatusCodes.Status200OK);
        g.MapPost("/{id:guid}/complete", (Guid id, PhotosHandler h, CancellationToken ct) => h.CompleteAsync(id, ct))
            .WithSummary("Verify the uploaded bytes and queue processing (idempotent).")
            .Produces<PhotoAssetDto>(StatusCodes.Status200OK);
        g.MapPost("/{id:guid}/reprocess", (Guid id, PhotosHandler h, CancellationToken ct) => h.ReprocessAsync(id, ct))
            .WithSummary("Re-queue a Ready or Failed asset through the processing pipeline.")
            .Produces<PhotoAssetDto>(StatusCodes.Status200OK);
        g.MapGet("/", (DateTimeOffset? from, DateTimeOffset? to, string? bbox, AssetKind? kind, AssetStatus? status, int? limit, string? cursor, PhotosHandler h, CancellationToken ct) =>
                h.ListAsync(from, to, bbox, kind, status, limit, cursor, ct))
            .WithSummary("List assets (keyset-paged, newest first) with presigned thumbnail URLs.")
            .Produces<PhotoListResponse>(StatusCodes.Status200OK);
        g.MapGet("/map", (string bbox, DateTimeOffset? from, DateTimeOffset? to, PhotosHandler h, CancellationToken ct) => h.MapAsync(bbox, from, to, ct))
            .WithSummary("Geotagged Ready assets in a viewport as a GeoJSON FeatureCollection.")
            .Produces<PhotoMapResponse>(StatusCodes.Status200OK);
        g.MapGet("/{id:guid}", (Guid id, PhotosHandler h, CancellationToken ct) => h.GetAsync(id, ct))
            .WithSummary("One asset with presigned original + thumbnail URLs.")
            .Produces<PhotoAssetDto>(StatusCodes.Status200OK);
        g.MapDelete("/{id:guid}", (Guid id, PhotosHandler h, CancellationToken ct) => h.DeleteAsync(id, ct))
            .WithSummary("Delete an asset: objects first, then the document.")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }
}
