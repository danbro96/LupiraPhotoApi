using LupiraPhotoApi.Application;
using LupiraPhotoApi.Auth;
using LupiraPhotoApi.Domain;
using LupiraPhotoApi.Dtos.Photos;
using LupiraPhotoApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraPhotoApi.Handlers;

public sealed class PhotosHandler(
    CurrentUser user,
    PhotoDeclareService declareService,
    PhotoCompleteService completeService,
    PhotoQueryService queryService,
    PhotoDeleteService deleteService)
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
        int? limit, string? cursor, CancellationToken ct)
    {
        Bbox? parsed = null;
        if (bbox is not null)
        {
            if (!Bbox.TryParse(bbox, out var b))
                return Problems.BadRequest("bbox must be minLon,minLat,maxLon,maxLat.");
            parsed = b;
        }
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await queryService.ListAsync(u.Id, from, to, parsed, kind, status, limit, cursor, ct));
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
}
