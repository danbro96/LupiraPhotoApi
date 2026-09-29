using LupiraPhotoApi.Core.Application.Processing;
using LupiraPhotoApi.Core.Application.Results;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Marten;

namespace LupiraPhotoApi.Core.Application;

/// <summary>Hand-set metadata: photographer and location. Manual outranks every derived source, so neither an
/// import re-run nor a reprocess undoes it.</summary>
public sealed class PhotoCurationService(IDocumentSession session, PhotoPresigner presigner, IReverseGeocoder reverseGeocoder)
{
    public const int RelocateMax = 2000;

    /// <summary>~1 m. A stale fix repeats to the last digit, so this only absorbs float round-trips.</summary>
    public const double CoordinateTolerance = 1e-5;

    public async Task<OpResult<PhotoAssetDto>> UpdateAsync(Guid principalId, Guid assetId, UpdatePhotoRequest req, CancellationToken ct)
    {
        var asset = await session.LoadAsync<PhotoAsset>(assetId, ct);
        if (asset is null || asset.PrincipalId != principalId) return OpResult<PhotoAssetDto>.NotFound();

        Photographer.SetManual(asset, req.CapturedByContactId);
        session.Store(asset);
        await session.SaveChangesAsync(ct);
        return OpResult<PhotoAssetDto>.Ok(await presigner.ToDtoAsync(asset, includeOriginal: false, ct));
    }

    public async Task<OpResult<PhotoAssetDto>> SetLocationAsync(Guid principalId, Guid assetId, SetPhotoLocationRequest req, CancellationToken ct)
    {
        if (CoordinateError(req.Latitude, req.Longitude) is { } error) return OpResult<PhotoAssetDto>.Invalid(error);

        var asset = await session.LoadAsync<PhotoAsset>(assetId, ct);
        if (asset is null || asset.PrincipalId != principalId) return OpResult<PhotoAssetDto>.NotFound();
        if (asset.Status == AssetStatus.Duplicate)
            return OpResult<PhotoAssetDto>.Conflict("A duplicate has no location of its own — set it on the canonical asset.");

        var label = Clean(req.Label);
        var placeLabel = label ?? await reverseGeocoder.ReverseLabelAsync(req.Latitude, req.Longitude, ct);
        ManualLocation.Set(asset, req.Latitude, req.Longitude, label, placeLabel, DateTimeOffset.UtcNow);
        session.Store(asset);
        await session.SaveChangesAsync(ct);
        return OpResult<PhotoAssetDto>.Ok(await presigner.ToDtoAsync(asset, includeOriginal: false, ct));
    }

    /// <summary>Drops the override and re-queues the asset so processing re-derives its geotag. Idempotent.</summary>
    public async Task<OpResult<PhotoAssetDto>> ClearLocationAsync(Guid principalId, Guid assetId, CancellationToken ct)
    {
        var asset = await session.LoadAsync<PhotoAsset>(assetId, ct);
        if (asset is null || asset.PrincipalId != principalId) return OpResult<PhotoAssetDto>.NotFound();

        if (ManualLocation.Clear(asset))
        {
            // An asset not yet Ready/Failed derives its geotag on the run it is already waiting for.
            AssetLifecycle.TryReprocess(asset);
            session.Store(asset);
            await session.SaveChangesAsync(ct);
        }

        return OpResult<PhotoAssetDto>.Ok(await presigner.ToDtoAsync(asset, includeOriginal: false, ct));
    }

    public async Task<OpResult<RelocatePhotosResponse>> RelocateAsync(Guid principalId, RelocatePhotosRequest req, CancellationToken ct)
    {
        if (CoordinateError(req.Latitude, req.Longitude) is { } error) return OpResult<RelocatePhotosResponse>.Invalid(error);
        var ids = req.Ids ?? [];
        if (ids.Count == 0 && (req.From is null || req.To is null))
            return OpResult<RelocatePhotosResponse>.Invalid("Select by ids, or by both from and to.");
        if (ids.Count > RelocateMax)
            return OpResult<RelocatePhotosResponse>.Invalid($"At most {RelocateMax} ids per call.");
        if (req.From > req.To)
            return OpResult<RelocatePhotosResponse>.Invalid("from must not be after to.");
        if ((req.AtLatitude is null) != (req.AtLongitude is null))
            return OpResult<RelocatePhotosResponse>.Invalid("atLatitude and atLongitude go together.");

        var query = session.Query<PhotoAsset>()
            .Where(a => a.PrincipalId == principalId && a.Status != AssetStatus.Duplicate && a.TrashedAt == null);
        if (ids.Count > 0) query = query.Where(a => ids.Contains(a.Id));
        if (req.From?.ToUniversalTime() is { } from) query = query.Where(a => a.TakenAt >= from);
        if (req.To?.ToUniversalTime() is { } to) query = query.Where(a => a.TakenAt <= to);
        if (Clean(req.CameraModel) is { } model)
            query = query.Where(a => a.Camera!.Model!.Equals(model, StringComparison.OrdinalIgnoreCase));
        if (req is { AtLatitude: { } atLat, AtLongitude: { } atLon })
        {
            // Marten can't translate arithmetic inside the predicate, so the bounds are locals.
            var (minLat, maxLat) = (atLat - CoordinateTolerance, atLat + CoordinateTolerance);
            var (minLon, maxLon) = (atLon - CoordinateTolerance, atLon + CoordinateTolerance);
            query = query.Where(a => a.Latitude >= minLat && a.Latitude <= maxLat && a.Longitude >= minLon && a.Longitude <= maxLon);
        }

        var assets = await query.OrderBy(a => a.TakenAt).Take(RelocateMax + 1).ToListAsync(ct);
        if (assets.Count > RelocateMax)
            return OpResult<RelocatePhotosResponse>.Invalid($"More than {RelocateMax} assets match — narrow the selector.");

        var matched = assets.Select(a => a.Id).ToList();
        if (req.DryRun)
            return OpResult<RelocatePhotosResponse>.Ok(new RelocatePhotosResponse { Count = matched.Count, Ids = matched });

        var label = Clean(req.Label);
        var placeLabel = label ?? await reverseGeocoder.ReverseLabelAsync(req.Latitude, req.Longitude, ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var asset in assets)
        {
            ManualLocation.Set(asset, req.Latitude, req.Longitude, label, placeLabel, now);
            session.Store(asset);
        }

        await session.SaveChangesAsync(ct);
        return OpResult<RelocatePhotosResponse>.Ok(new RelocatePhotosResponse { Count = matched.Count, Ids = matched, PlaceLabel = placeLabel });
    }

    private static string? CoordinateError(double latitude, double longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180
            ? null
            : "latitude must be within ±90 and longitude within ±180.";

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
