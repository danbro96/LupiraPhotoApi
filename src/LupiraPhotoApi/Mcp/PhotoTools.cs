using System.ComponentModel;
using LupiraPhotoApi.Auth;
using LupiraPhotoApi.Core.Application;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace LupiraPhotoApi.Mcp;

/// <summary>
/// The agent's MCP surface over the caller's own library: reads plus hand-set location corrections, via the
/// SAME Core services as the REST handlers. Thumbnail URLs in results are presigned and time-limited.
/// </summary>
[McpServerToolType]
public sealed class PhotoTools(CurrentUser user, PhotoQueryService query, PhotoStatsService stats, PhotoCurationService curation)
{
    [McpServerTool(Name = "list_photos")]
    [Description("List the caller's photo/video assets in a time window (newest first), optionally filtered to a bbox or kind. Items carry presigned thumbnail URLs.")]
    public async Task<PhotoListResponse> ListPhotos(
        [Description("Window start, ISO-8601.")] DateTimeOffset? from = null,
        [Description("Window end, ISO-8601.")] DateTimeOffset? to = null,
        [Description("Viewport as minLon,minLat,maxLon,maxLat.")] string? bbox = null,
        [Description("Photo or Video.")] AssetKind? kind = null,
        [Description("Max items (default 100, cap 500).")] int? limit = null,
        [Description("Opaque cursor from a previous page.")] string? cursor = null,
        CancellationToken ct = default)
    {
        Bbox? parsed = null;
        if (bbox is not null)
        {
            if (!Bbox.TryParse(bbox, out var b)) throw new McpException("bbox must be minLon,minLat,maxLon,maxLat.");
            parsed = b;
        }

        var pid = (await user.GetAsync(ct)).Id;
        var result = await query.ListAsync(pid, from, to, parsed, kind, null, null, null, null, null, null, limit, cursor, ct);
        return result.IsOk ? result.Value! : throw new McpException(result.Error ?? result.Status.ToString());
    }

    [McpServerTool(Name = "search_photos")]
    [Description("Search the caller's assets by place label substring within a time window.")]
    public async Task<List<PhotoListItemDto>> SearchPhotos(
        [Description("Case-insensitive substring matched against the geotag place label.")] string place,
        [Description("Window start, ISO-8601.")] DateTimeOffset? from = null,
        [Description("Window end, ISO-8601.")] DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var pid = (await user.GetAsync(ct)).Id;
        // Server-side filter: this used to page the newest 500 and match in memory, so anything older
        // than that window was simply unfindable.
        var result = await query.ListAsync(pid, from, to, null, null, null, null, place, null, null, null, PhotoQueryService.MaxLimit, null, ct);
        if (!result.IsOk) throw new McpException(result.Error ?? result.Status.ToString());
        return result.Value!.Items;
    }

    [McpServerTool(Name = "photo_stats")]
    [Description("Library stats for the caller: totals, byte size, and counts by kind/status/geotag-source/month.")]
    public async Task<PhotoStats> Stats(CancellationToken ct = default)
    {
        var pid = (await user.GetAsync(ct)).Id;
        return await stats.GetAsync(pid, ct);
    }

    [McpServerTool(Name = "relocate_photos")]
    [Description("Hand-set one location on every asset the selector matches — e.g. fix a camera's stale GPS fix. Select by ids, or by from+to narrowed by cameraModel and/or the current coordinate (atLatitude+atLongitude, ~1 m). The location outranks the file's GPS and survives reprocessing; clear_photo_location undoes it. Preview with dryRun=true first.")]
    public async Task<RelocatePhotosResponse> RelocatePhotos(
        [Description("New latitude.")] double latitude,
        [Description("New longitude.")] double longitude,
        [Description("Curated place label; omit to reverse-geocode.")] string? label = null,
        [Description("Asset ids (max 2000).")] List<Guid>? ids = null,
        [Description("Window start, ISO-8601 (required with to when no ids).")] DateTimeOffset? from = null,
        [Description("Window end, ISO-8601.")] DateTimeOffset? to = null,
        [Description("EXIF camera model, case-insensitive (e.g. GT-I9300).")] string? cameraModel = null,
        [Description("Only assets currently at this latitude.")] double? atLatitude = null,
        [Description("Only assets currently at this longitude.")] double? atLongitude = null,
        [Description("Report the matches without changing anything.")] bool dryRun = false,
        CancellationToken ct = default)
    {
        var pid = (await user.GetAsync(ct)).Id;
        var request = new RelocatePhotosRequest
        {
            Latitude = latitude,
            Longitude = longitude,
            Label = label,
            Ids = ids,
            From = from,
            To = to,
            CameraModel = cameraModel,
            AtLatitude = atLatitude,
            AtLongitude = atLongitude,
            DryRun = dryRun,
        };
        var result = await curation.RelocateAsync(pid, request, ct);
        return result.IsOk ? result.Value! : throw new McpException(result.Error ?? result.Status.ToString());
    }

    [McpServerTool(Name = "clear_photo_location")]
    [Description("Drop an asset's hand-set location and re-queue it so its geotag is re-derived from the file, hint and history.")]
    public async Task<PhotoAssetDto> ClearPhotoLocation(
        [Description("Asset id.")] Guid id,
        CancellationToken ct = default)
    {
        var pid = (await user.GetAsync(ct)).Id;
        var result = await curation.ClearLocationAsync(pid, id, ct);
        return result.IsOk ? result.Value! : throw new McpException(result.Error ?? result.Status.ToString());
    }
}
