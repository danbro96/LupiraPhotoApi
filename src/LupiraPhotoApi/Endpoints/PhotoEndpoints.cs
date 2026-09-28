using LupiraPhotoApi.Core.Application;
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
            .WithName("DeclarePhoto")
            .WithSummary("Declare an asset (idempotent) and receive a presigned upload URL while bytes are pending.")
            .Produces<DeclaredPhotoResponse>(StatusCodes.Status200OK);
        g.MapPost("/{id:guid}/complete", (Guid id, PhotosHandler h, CancellationToken ct) => h.CompleteAsync(id, ct))
            .WithName("CompletePhotoUpload")
            .WithSummary("Verify the uploaded bytes and queue processing (idempotent).")
            .Produces<PhotoAssetDto>(StatusCodes.Status200OK);
        g.MapPost("/{id:guid}/reprocess", (Guid id, PhotosHandler h, CancellationToken ct) => h.ReprocessAsync(id, ct))
            .WithName("ReprocessPhoto")
            .WithSummary("Re-queue a Ready or Failed asset through the processing pipeline.")
            .Produces<PhotoAssetDto>(StatusCodes.Status200OK);
        g.MapGet("/", (DateTimeOffset? from, DateTimeOffset? to, string? bbox, AssetKind? kind, AssetStatus? status, bool? located, string? place, string? sourceAlbum, PhotoSort? sort, int? limit, string? cursor, PhotosHandler h, CancellationToken ct) =>
                h.ListAsync(from, to, bbox, kind, status, located, place, sourceAlbum, sort, limit, cursor, ct))
            .WithName("ListPhotos")
            .WithSummary("List assets (keyset-paged, newest taken first by default) with presigned thumbnail URLs.")
            .Produces<PhotoListResponse>(StatusCodes.Status200OK);
        g.MapPost("/lookup", (LookupPhotosRequest body, PhotosHandler h, CancellationToken ct) => h.LookupAsync(body, ct))
            .WithName("LookupPhotos")
            .WithSummary("Hydrate up to 200 assets by id — turns relation references into renderable items.")
            .Produces<PhotoListResponse>(StatusCodes.Status200OK);
        g.MapGet("/stats", (PhotosHandler h, CancellationToken ct) => h.StatsAsync(ct))
            .WithName("GetPhotoStats")
            .WithSummary("Library totals and counts by kind, status, geotag source and month.")
            .Produces<PhotoStats>(StatusCodes.Status200OK);
        g.MapGet("/albums", (PhotosHandler h, CancellationToken ct) => h.AlbumsAsync(ct))
            .WithName("ListPhotoAlbums")
            .WithSummary("Imported event folders and albums with their core date span, for linking to calendar events.")
            .Produces<List<PhotoAlbumDto>>(StatusCodes.Status200OK);
        g.MapGet("/map", (string bbox, DateTimeOffset? from, DateTimeOffset? to, PhotosHandler h, CancellationToken ct) => h.MapAsync(bbox, from, to, ct))
            .WithName("GetPhotoMap")
            .WithSummary("Geotagged Ready assets in a viewport as a GeoJSON FeatureCollection.")
            .Produces<PhotoMapResponse>(StatusCodes.Status200OK);
        g.MapGet("/{id:guid}", (Guid id, PhotosHandler h, CancellationToken ct) => h.GetAsync(id, ct))
            .WithName("GetPhoto")
            .WithSummary("One asset with presigned original + thumbnail URLs.")
            .Produces<PhotoAssetDto>(StatusCodes.Status200OK);
        g.MapPatch("/{id:guid}", (Guid id, UpdatePhotoRequest body, PhotosHandler h, CancellationToken ct) => h.UpdateAsync(id, body, ct))
            .WithName("UpdatePhoto")
            .WithSummary("Hand-set metadata (the photographer); outranks anything derived.")
            .Produces<PhotoAssetDto>(StatusCodes.Status200OK);
        g.MapDelete("/{id:guid}", (Guid id, PhotosHandler h, CancellationToken ct) => h.DeleteAsync(id, ct))
            .WithName("DeletePhoto")
            .WithSummary("Delete an asset: objects first, then the document.")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }
}
