using LupiraPhotoApi.Auth;
using LupiraPhotoApi.Core.Application;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using LupiraPhotoApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraPhotoApi.Handlers;

public sealed class PhotosHandler(
    CurrentUser user,
    PhotoDeclareService declareService,
    PhotoCompleteService completeService,
    PhotoQueryService queryService,
    PhotoStatsService statsService,
    PhotoDeleteService deleteService,
    PhotoCurationService curationService,
    PhotoAlbumService albumService)
{
    public async Task<Results<Ok<DeclaredPhotoResponse>, ProblemHttpResult, UnauthorizedHttpResult>> DeclareAsync(
        DeclarePhotoRequest request, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await declareService.DeclareAsync(u.Id, request, ct));
    }

    public async Task<Results<Ok<PhotoAssetDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> CompleteAsync(
        Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await completeService.CompleteAsync(u.Id, id, ct));
    }

    public async Task<Results<Ok<PhotoAssetDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> ReprocessAsync(
        Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await completeService.ReprocessAsync(u.Id, id, ct));
    }

    public async Task<Results<Ok<PhotoListResponse>, ProblemHttpResult, UnauthorizedHttpResult>> ListAsync(
        DateTimeOffset? from, DateTimeOffset? to, string? bbox, AssetKind? kind, AssetStatus? status,
        bool? located, string? place, string? sourceAlbum, PhotoSort? sort, int? limit, string? cursor, CancellationToken ct)
    {
        Bbox? parsed = null;
        if (bbox is not null)
        {
            if (!Bbox.TryParse(bbox, out var b))
                return Problems.BadRequest("bbox must be minLon,minLat,maxLon,maxLat.");
            parsed = b;
        }

        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(
            await queryService.ListAsync(u.Id, from, to, parsed, kind, status, located, place, sourceAlbum, sort, limit, cursor, ct));
    }

    public async Task<Results<Ok<PhotoListResponse>, ProblemHttpResult, UnauthorizedHttpResult>> LookupAsync(
        LookupPhotosRequest request, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await queryService.LookupAsync(u.Id, request.Ids, ct));
    }

    public async Task<Results<Ok<PhotoStats>, ProblemHttpResult, UnauthorizedHttpResult>> StatsAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return TypedResults.Ok(await statsService.GetAsync(u.Id, ct));
    }

    public async Task<Results<Ok<PhotoMapResponse>, ProblemHttpResult, UnauthorizedHttpResult>> MapAsync(
        string bbox, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        if (!Bbox.TryParse(bbox, out var parsed))
            return Problems.BadRequest("bbox must be minLon,minLat,maxLon,maxLat.");
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await queryService.MapAsync(u.Id, parsed, from, to, ct));
    }

    public async Task<Results<Ok<PhotoAssetDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> GetAsync(
        Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await queryService.GetAsync(u.Id, id, ct));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> DeleteAsync(
        Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await deleteService.DeleteAsync(u.Id, id, ct));
    }

    public async Task<Results<Ok<PhotoAssetDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> UpdateAsync(
        Guid id, UpdatePhotoRequest request, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await curationService.UpdateAsync(u.Id, id, request, ct));
    }

    public async Task<Results<Ok<List<PhotoAlbumDto>>, UnauthorizedHttpResult>> AlbumsAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return TypedResults.Ok(await albumService.ListAsync(u.Id, ct));
    }
}
