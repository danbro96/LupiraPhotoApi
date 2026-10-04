using LupiraPhotoApi.Core.Application.Import;
using LupiraPhotoApi.Core.Application.Results;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Marten;

namespace LupiraPhotoApi.Core.Application;

/// <summary>Finds GPS fixes a camera can't have taken. A spike is a fix both of its neighbours would have had to
/// fly to while they sit close to each other. A repeat is one coordinate on several days — a stale fix, but also
/// what a home looks like, so it is rejected only when named.</summary>
public sealed class GpsSweepService(IDocumentSession session, PhotoPresigner presigner)
{
    public const double SpikeKmh = 1000;
    public const int SampleLimit = 50;
    public const int RejectedIdLimit = 2000;

    private static readonly TimeSpan NeighbourWindow = TimeSpan.FromHours(6);

    // Burst shots share a second; without a floor any jitter between them reads as supersonic.
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(60);

    public async Task<OpResult<GpsSweepResponse>> SweepAsync(Guid principalId, GpsSweepRequest req, CancellationToken ct)
    {
        if (req.From > req.To)
            return OpResult<GpsSweepResponse>.Invalid("from must not be after to.");
        var rejectCoordinates = req.RejectCoordinates ?? [];
        if (rejectCoordinates.Any(c => c.Latitude is < -90 or > 90 || c.Longitude is < -180 or > 180))
            return OpResult<GpsSweepResponse>.Invalid("latitude must be within ±90 and longitude within ±180.");

        var query = session.Query<PhotoAsset>()
            .Where(a => a.PrincipalId == principalId && a.Status == AssetStatus.Ready && a.TrashedAt == null
                     && a.GeotagSource == GeotagSource.ExifGps && a.Latitude != null && a.Longitude != null);
        if (req.From?.ToUniversalTime() is { } from) query = query.Where(a => a.TakenAt >= from);
        if (req.To?.ToUniversalTime() is { } to) query = query.Where(a => a.TakenAt <= to);
        var rows = await query
            .Select(a => new { a.Id, a.DeviceId, a.Camera, a.TakenAt, a.TakenAtSource, a.Latitude, a.Longitude })
            .ToListAsync(ct);

        var fixes = new List<GpsFix>();
        foreach (var row in rows)
        {
            if (CaptureTime.IsApproximate(row.TakenAtSource) || CameraKey(row.DeviceId, row.Camera) is not { } camera) continue;
            fixes.Add(new GpsFix(row.Id, camera, row.TakenAt, row.Latitude!.Value, row.Longitude!.Value));
        }

        var spikes = FindSpikes(fixes);
        var repeats = FindRepeats(fixes);
        var rejected = new List<Guid>();
        if (req.Apply && Rejections(fixes, spikes, rejectCoordinates) is { Count: > 0 } rejections)
        {
            var now = DateTimeOffset.UtcNow;
            var ids = rejections.Keys.ToList();
            foreach (var asset in await session.Query<PhotoAsset>().Where(a => ids.Contains(a.Id)).ToListAsync(ct))
            {
                // A legacy phone fix lives only in Latitude/Longitude, which reprocessing clears; keep it for a restore.
                asset.PlaceHint ??= PlaceHint.LegacyDevice(asset);
                asset.GpsRejection = new GpsRejection
                {
                    Latitude = asset.Latitude!.Value,
                    Longitude = asset.Longitude!.Value,
                    Reason = rejections[asset.Id],
                    At = now,
                };
                AssetLifecycle.TryReprocess(asset);
                session.Store(asset);
                rejected.Add(asset.Id);
            }

            await session.SaveChangesAsync(ct);
        }

        return OpResult<GpsSweepResponse>.Ok(new GpsSweepResponse
        {
            Scanned = fixes.Count,
            SpikeCount = spikes.Count,
            Spikes = [.. spikes.Take(SampleLimit)],
            RepeatCount = repeats.Count,
            Repeats = [.. repeats.Take(SampleLimit)],
            Rejected = rejected.Count,
            RejectedIds = [.. rejected.Take(RejectedIdLimit)],
        });
    }

    /// <summary>Drops the rejection and re-queues the asset so the fix is used again. Idempotent.</summary>
    public async Task<OpResult<PhotoAssetDto>> RestoreAsync(Guid principalId, Guid assetId, CancellationToken ct)
    {
        var asset = await session.LoadAsync<PhotoAsset>(assetId, ct);
        if (asset is null || asset.PrincipalId != principalId) return OpResult<PhotoAssetDto>.NotFound();

        if (asset.GpsRejection is not null)
        {
            asset.GpsRejection = null;
            AssetLifecycle.TryReprocess(asset);
            session.Store(asset);
            await session.SaveChangesAsync(ct);
        }

        return OpResult<PhotoAssetDto>.Ok(await presigner.ToDtoAsync(asset, includeOriginal: false, ct));
    }

    /// <summary>The ids that held a rejection; unknown ids and ones without a rejection are left out.</summary>
    public async Task<OpResult<List<Guid>>> RestoreManyAsync(Guid principalId, List<Guid> ids, CancellationToken ct)
    {
        if (ids.Count > PhotoCurationService.RelocateMax)
            return OpResult<List<Guid>>.Invalid($"At most {PhotoCurationService.RelocateMax} ids per call.");

        var assets = await session.Query<PhotoAsset>()
            .Where(a => a.PrincipalId == principalId && ids.Contains(a.Id) && a.GpsRejection != null)
            .ToListAsync(ct);
        foreach (var asset in assets)
        {
            asset.GpsRejection = null;
            AssetLifecycle.TryReprocess(asset);
            session.Store(asset);
        }

        await session.SaveChangesAsync(ct);
        return OpResult<List<Guid>>.Ok([.. assets.Select(a => a.Id)]);
    }

    public static bool IsRejected(PhotoAsset asset, double latitude, double longitude) =>
        asset.GpsRejection is { } r && Near(r.Latitude, r.Longitude, latitude, longitude);

    /// <summary>Phones are one track per device; imports mix cameras, so they split by camera and drop the unknown.</summary>
    internal static string? CameraKey(string deviceId, CameraInfo? camera) =>
        !deviceId.StartsWith("import:", StringComparison.Ordinal) ? deviceId
        : DtoMapping.CameraName(camera) is { } name ? ImportMapParser.Collapse(name)
        : null;

    internal static List<GpsSpikeDto> FindSpikes(IEnumerable<GpsFix> fixes)
    {
        var spikes = new List<GpsSpikeDto>();
        foreach (var camera in fixes.GroupBy(f => f.Camera))
        {
            var track = camera.OrderBy(f => f.TakenAt).ThenBy(f => f.Id).ToList();
            for (var i = 1; i < track.Count - 1; i++)
            {
                var (prev, fix, next) = (track[i - 1], track[i], track[i + 1]);
                if (fix.TakenAt - prev.TakenAt > NeighbourWindow || next.TakenAt - fix.TakenAt > NeighbourWindow) continue;
                var (speedIn, speedOut) = (Kmh(prev, fix), Kmh(fix, next));
                if (speedIn <= SpikeKmh || speedOut <= SpikeKmh || Kmh(prev, next) > SpikeKmh) continue;
                spikes.Add(new GpsSpikeDto
                {
                    Id = fix.Id,
                    TakenAt = fix.TakenAt,
                    Latitude = fix.Latitude,
                    Longitude = fix.Longitude,
                    Camera = fix.Camera,
                    SpeedInKmh = Math.Round(speedIn),
                    SpeedOutKmh = Math.Round(speedOut),
                });
            }
        }

        return [.. spikes.OrderBy(s => s.TakenAt)];
    }

    internal static List<GpsRepeatDto> FindRepeats(IEnumerable<GpsFix> fixes) =>
        [.. fixes
            .GroupBy(f => (f.Camera, Lat: Cell(f.Latitude), Lon: Cell(f.Longitude)))
            .Select(g => (Fixes: g.ToList(), Days: g.Select(f => DateOnly.FromDateTime(f.TakenAt.UtcDateTime)).Distinct().Count()))
            .Where(g => g.Days >= 2)
            .Select(g => new GpsRepeatDto
            {
                Latitude = g.Fixes[0].Latitude,
                Longitude = g.Fixes[0].Longitude,
                Camera = g.Fixes[0].Camera,
                Count = g.Fixes.Count,
                Days = g.Days,
                First = g.Fixes.Min(f => f.TakenAt),
                Last = g.Fixes.Max(f => f.TakenAt),
            })
            .OrderByDescending(r => r.Days)
            .ThenByDescending(r => r.Count)
            .ThenBy(r => r.Camera, StringComparer.Ordinal)];

    internal static Dictionary<Guid, GpsRejectionReason> Rejections(
        IEnumerable<GpsFix> fixes, IEnumerable<GpsSpikeDto> spikes, IReadOnlyCollection<GpsCoordinateDto> rejectCoordinates)
    {
        var rejections = spikes.ToDictionary(s => s.Id, _ => GpsRejectionReason.Spike);
        foreach (var fix in fixes)
        {
            if (rejectCoordinates.Any(c => Near(c.Latitude, c.Longitude, fix.Latitude, fix.Longitude)))
                rejections.TryAdd(fix.Id, GpsRejectionReason.Repeat);
        }

        return rejections;
    }

    private static double Kmh(GpsFix from, GpsFix to)
    {
        var elapsed = (to.TakenAt - from.TakenAt).Duration();
        var hours = (elapsed < MinInterval ? MinInterval : elapsed).TotalHours;
        return GeoMath.DistanceMeters(from.Latitude, from.Longitude, to.Latitude, to.Longitude) / 1000 / hours;
    }

    private static long Cell(double degrees) => (long)Math.Round(degrees / PhotoCurationService.CoordinateTolerance);

    private static bool Near(double lat1, double lon1, double lat2, double lon2) =>
        Math.Abs(lat1 - lat2) <= PhotoCurationService.CoordinateTolerance && Math.Abs(lon1 - lon2) <= PhotoCurationService.CoordinateTolerance;
}
