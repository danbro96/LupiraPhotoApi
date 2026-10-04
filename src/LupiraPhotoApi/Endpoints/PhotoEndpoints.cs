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
        g.MapPost("/{id:guid}/trash", (Guid id, PhotosHandler h, CancellationToken ct) => h.TrashAsync(id, ct))
            .WithName("TrashPhoto")
            .WithSummary("Move an asset to the trash (idempotent). It keeps its bytes and status until restored or purged.")
            .Produces<PhotoAssetDto>(StatusCodes.Status200OK);
        g.MapPost("/{id:guid}/restore", (Guid id, PhotosHandler h, CancellationToken ct) => h.RestoreAsync(id, ct))
            .WithName("RestorePhoto")
            .WithSummary("Take an asset back out of the trash (idempotent).")
            .Produces<PhotoAssetDto>(StatusCodes.Status200OK);
        g.MapGet("/", (DateTimeOffset? from, DateTimeOffset? to, string? bbox, AssetKind? kind, AssetStatus? status, bool? located, string? place, string? sourceAlbum, bool? trashed, PhotoSort? sort, int? limit, string? cursor, PhotosHandler h, CancellationToken ct) =>
                h.ListAsync(from, to, bbox, kind, status, located, place, sourceAlbum, trashed, sort, limit, cursor, ct))
            .WithName("ListPhotos")
            .WithSummary("List assets (keyset-paged, newest taken first by default) with presigned thumbnail URLs. trashed=true lists only the trash.")
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
        g.MapGet("/places", (string? q, int? limit, PhotosHandler h, CancellationToken ct) => h.PlacesAsync(q, limit, ct))
            .WithName("ListPhotoPlaces")
            .WithSummary("Place labels by asset count, most used first — suggestions for the place filter (q = substring).")
            .Produces<List<PhotoPlaceCount>>(StatusCodes.Status200OK);
        g.MapGet("/map", (string bbox, double? zoom, DateTimeOffset? from, DateTimeOffset? to, PhotosHandler h, CancellationToken ct) => h.MapAsync(bbox, zoom, from, to, ct))
            .WithName("GetPhotoMap")
            .WithSummary("Geotagged Ready assets in a viewport as GeoJSON: a point per photo when at most 200 are in view or zoom >= 17, else a point per grid cell with its count.")
            .Produces<PhotoMapResponse>(StatusCodes.Status200OK);
        g.MapGet("/density", (DateTimeOffset? from, DateTimeOffset? to, PhotosHandler h, CancellationToken ct) => h.DensityAsync(from, to, ct))
            .WithName("GetPhotoDensity")
            .WithSummary("Measured-location Ready photos per ~100 m cell with their distinct UTC days, most days first.")
            .Produces<List<PhotoDensityCellDto>>(StatusCodes.Status200OK);
        g.MapGet("/{id:guid}", (Guid id, PhotosHandler h, CancellationToken ct) => h.GetAsync(id, ct))
            .WithName("GetPhoto")
            .WithSummary("One asset with presigned original + thumbnail URLs.")
            .Produces<PhotoAssetDto>(StatusCodes.Status200OK);
        g.MapPatch("/{id:guid}", (Guid id, UpdatePhotoRequest body, PhotosHandler h, CancellationToken ct) => h.UpdateAsync(id, body, ct))
            .WithName("UpdatePhoto")
            .WithSummary("Hand-set metadata (the photographer); outranks anything derived.")
            .Produces<PhotoAssetDto>(StatusCodes.Status200OK);
        g.MapPut("/{id:guid}/location", (Guid id, SetPhotoLocationRequest body, PhotosHandler h, CancellationToken ct) => h.SetLocationAsync(id, body, ct))
            .WithName("SetPhotoLocation")
            .WithSummary("Hand-set the location; outranks the file's own GPS and survives reprocessing.")
            .Produces<PhotoAssetDto>(StatusCodes.Status200OK);
        g.MapDelete("/{id:guid}/location", (Guid id, PhotosHandler h, CancellationToken ct) => h.ClearLocationAsync(id, ct))
            .WithName("ClearPhotoLocation")
            .WithSummary("Drop a hand-set location and re-queue the asset so its geotag is re-derived (idempotent).")
            .Produces<PhotoAssetDto>(StatusCodes.Status200OK);
        g.MapPost("/relocate", (RelocatePhotosRequest body, PhotosHandler h, CancellationToken ct) => h.RelocateAsync(body, ct))
            .WithName("RelocatePhotos")
            .WithSummary("Hand-set one location on every asset a selector matches (ids, or a time window narrowed by camera and current coordinate). dryRun previews.")
            .Produces<RelocatePhotosResponse>(StatusCodes.Status200OK);
        g.MapPut("/{id:guid}/taken-at", (Guid id, SetPhotoTakenAtRequest body, PhotosHandler h, CancellationToken ct) => h.SetTakenAtAsync(id, body, ct))
            .WithName("SetPhotoTakenAt")
            .WithSummary("Hand-set the capture time; outranks every derived time and survives import re-runs.")
            .Produces<PhotoAssetDto>(StatusCodes.Status200OK);
        g.MapDelete("/{id:guid}/taken-at", (Guid id, PhotosHandler h, CancellationToken ct) => h.ClearTakenAtAsync(id, ct))
            .WithName("ClearPhotoTakenAt")
            .WithSummary("Drop a hand-set capture time and restore the derived one (idempotent).")
            .Produces<PhotoAssetDto>(StatusCodes.Status200OK);
        g.MapPost("/retime", (RetimePhotosRequest body, PhotosHandler h, CancellationToken ct) => h.RetimeAsync(body, ct))
            .WithName("RetimePhotos")
            .WithSummary("Shift or set the capture time of every asset a selector matches (ids, or a time window narrowed by camera and device). dryRun previews before/after.")
            .Produces<RetimePhotosResponse>(StatusCodes.Status200OK);
        g.MapPost("/gps-sweep", (GpsSweepRequest body, PhotosHandler h, CancellationToken ct) => h.GpsSweepAsync(body, ct))
            .WithName("SweepPhotoGps")
            .WithSummary("Report impossible GPS fixes per camera: speed spikes and coordinates repeated across days. apply rejects the spikes and the listed coordinates, and re-queues them.")
            .Produces<GpsSweepResponse>(StatusCodes.Status200OK);
        g.MapDelete("/{id:guid}/gps-rejection", (Guid id, PhotosHandler h, CancellationToken ct) => h.RestoreGpsAsync(id, ct))
            .WithName("RestorePhotoGps")
            .WithSummary("Drop a GPS rejection and re-queue the asset so its fix is used again (idempotent).")
            .Produces<PhotoAssetDto>(StatusCodes.Status200OK);
        g.MapDelete("/{id:guid}", (Guid id, PhotosHandler h, CancellationToken ct) => h.DeleteAsync(id, ct))
            .WithName("DeletePhoto")
            .WithSummary("Delete an asset permanently, trashed or not: objects first, then the document.")
            .Produces(StatusCodes.Status204NoContent);
        g.MapDelete("/trash", (PhotosHandler h, CancellationToken ct) => h.EmptyTrashAsync(ct))
            .WithName("EmptyPhotoTrash")
            .WithSummary("Permanently delete every trashed asset.")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }
}
