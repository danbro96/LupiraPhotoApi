using System.ComponentModel;
using Lupira.Mcp;
using LupiraPhotoApi.Auth;
using LupiraPhotoApi.Core.Application;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace LupiraPhotoApi.Mcp;

/// <summary>
/// The agent's MCP surface over the caller's own library: reads plus hand-set location and time corrections and the
/// GPS sweep, via the SAME Core services as the REST handlers. Thumbnail URLs in results are presigned and time-limited.
/// </summary>
[McpServerToolType]
public sealed class PhotoTools(CurrentUser user, PhotoQueryService query, PhotoStatsService stats, PhotoCurationService curation, GpsSweepService gpsSweep)
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
        return result.Require();
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
        return result.Require().Items;
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
        return result.Require();
    }

    [McpServerTool(Name = "clear_photo_location")]
    [Description("Drop an asset's hand-set location and re-queue it so its geotag is re-derived from the file, hint and history.")]
    public async Task<PhotoAssetDto> ClearPhotoLocation(
        [Description("Asset id.")] Guid id,
        CancellationToken ct = default)
    {
        var pid = (await user.GetAsync(ct)).Id;
        var result = await curation.ClearLocationAsync(pid, id, ct);
        return result.Require();
    }

    [McpServerTool(Name = "retime_photos")]
    [Description("Hand-set the capture time of every asset the selector matches — e.g. a camera whose clock ran in the wrong zone. Give exactly one of shiftBy or setTo. Select by ids, or by from+to narrowed by cameraModel and/or deviceId. The time outranks the file's and survives import re-runs; a photo placed from location history is re-queued to follow the new time. clear_photo_time undoes it per asset. Preview with dryRun=true first: it lists before/after.")]
    public async Task<RetimePhotosResponse> RetimePhotos(
        [Description("Signed shift added to each current time, as [-][d.]hh:mm:ss (e.g. -01:00:00).")] TimeSpan? shiftBy = null,
        [Description("One ISO-8601 time for every match.")] DateTimeOffset? setTo = null,
        [Description("Asset ids (max 2000).")] List<Guid>? ids = null,
        [Description("Window start on the current time, ISO-8601 (required with to when no ids).")] DateTimeOffset? from = null,
        [Description("Window end, ISO-8601.")] DateTimeOffset? to = null,
        [Description("EXIF camera model, case-insensitive (e.g. HTC Desire).")] string? cameraModel = null,
        [Description("Uploading device id, or import:<source> for an import.")] string? deviceId = null,
        [Description("Report before/after without changing anything.")] bool dryRun = false,
        CancellationToken ct = default)
    {
        var pid = (await user.GetAsync(ct)).Id;
        var request = new RetimePhotosRequest
        {
            ShiftBy = shiftBy,
            SetTo = setTo,
            Ids = ids,
            From = from,
            To = to,
            CameraModel = cameraModel,
            DeviceId = deviceId,
            DryRun = dryRun,
        };
        var result = await curation.RetimeAsync(pid, request, ct);
        return result.Require();
    }

    [McpServerTool(Name = "clear_photo_time")]
    [Description("Drop an asset's hand-set capture time and restore the one derived from its file.")]
    public async Task<PhotoAssetDto> ClearPhotoTime(
        [Description("Asset id.")] Guid id,
        CancellationToken ct = default)
    {
        var pid = (await user.GetAsync(ct)).Id;
        var result = await curation.ClearTakenAtAsync(pid, id, ct);
        return result.Require();
    }

    [McpServerTool(Name = "sweep_photo_gps")]
    [Description("Find impossible GPS fixes per camera among photos with exact times: spikes (>1000 km/h to and from both neighbours, which sit close to each other) and coordinates repeated on several days. Reports counts and capped samples. apply=true rejects every spike plus the repeats named in rejectCoordinates, re-queues them and returns their ids (rejectedIds); a rejected photo falls back to location history, else no location. Run after fixing clocks with retime_photos; restore_photo_gps undoes.")]
    public async Task<GpsSweepResponse> SweepPhotoGps(
        [Description("Window start, ISO-8601.")] DateTimeOffset? from = null,
        [Description("Window end, ISO-8601.")] DateTimeOffset? to = null,
        [Description("Reject and re-queue; default is a dry run.")] bool apply = false,
        [Description("Repeated coordinates to reject too (within ~1 m), copied from the report's repeats.")] List<GpsCoordinateDto>? rejectCoordinates = null,
        CancellationToken ct = default)
    {
        var pid = (await user.GetAsync(ct)).Id;
        var request = new GpsSweepRequest { From = from, To = to, Apply = apply, RejectCoordinates = rejectCoordinates };
        var result = await gpsSweep.SweepAsync(pid, request, ct);
        return result.Require();
    }

    [McpServerTool(Name = "restore_photo_gps")]
    [Description("Drop the GPS rejection on these assets and re-queue them so their own fix is used again. Returns the ids that held a rejection.")]
    public async Task<List<Guid>> RestorePhotoGps(
        [Description("Asset ids (max 2000).")] List<Guid> ids,
        CancellationToken ct = default)
    {
        var pid = (await user.GetAsync(ct)).Id;
        var result = await gpsSweep.RestoreManyAsync(pid, ids, ct);
        return result.Require();
    }
}
