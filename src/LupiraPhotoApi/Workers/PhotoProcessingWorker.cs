using Lupira.Identity.Marten;
using LupiraPhotoApi.Core.Application;
using LupiraPhotoApi.Core.Application.Processing;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Storage;
using Marten;
using Microsoft.Extensions.Options;

namespace LupiraPhotoApi.Workers;

/// <summary>The processing loop: claims Uploaded assets (plus expired-lease Processing ones — crash
/// recovery) and runs the thumbnail + geotag pipeline. Single replica at family scale; the lease is
/// crash recovery, not contention control. One bad tick never kills the loop. Hourly sub-tick expires
/// stale Declared assets.</summary>
public sealed class PhotoProcessingWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<PhotoOptions> options,
    ILogger<PhotoProcessingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan JanitorEvery = TimeSpan.FromHours(1);

    private readonly PhotoOptions _opts = options.Value;
    private DateTimeOffset _lastJanitorRun = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Startup jitter so a fleet restart doesn't align every tick with the schema apply / readyz storm.
        await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(2, 8)), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_opts.ProcessingTickSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Photo processing tick failed.");
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
        var pipeline = scope.ServiceProvider.GetRequiredService<PhotoProcessingService>();
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();
        var now = DateTimeOffset.UtcNow;

        var claimable = await session.Query<PhotoAsset>()
            .Where(a => (a.Status == AssetStatus.Uploaded && (a.NextAttemptAt == null || a.NextAttemptAt <= now))
                     || (a.Status == AssetStatus.Processing && a.LeaseUntil != null && a.LeaseUntil < now))
            .OrderBy(a => a.UploadedAt)
            .Take(_opts.ProcessingBatchSize)
            .ToListAsync(ct);

        foreach (var asset in claimable)
        {
            if (!AssetLifecycle.TryClaim(asset, DateTimeOffset.UtcNow)) continue;
            session.Store(asset);
            await session.SaveChangesAsync(ct);

            using var activity = PhotoTelemetry.Source.StartActivity("photo.process");
            activity?.SetTag("asset.id", asset.Id);
            try
            {
                var owner = await session.LoadAsync<Principal>(asset.PrincipalId, ct)
                    ?? throw new InvalidOperationException($"Principal {asset.PrincipalId} not found.");
                await pipeline.ProcessAsync(asset, owner.AuthentikSub, ct);
                AssetLifecycle.TryCompleteProcessing(asset, DateTimeOffset.UtcNow);
                if (await TryDedupeAsync(session, store, asset, ct))
                {
                    logger.LogInformation("Asset {AssetId} is a duplicate of {CanonicalId}; objects deleted.", asset.Id, asset.DuplicateOfId);
                }
                else
                {
                    logger.LogInformation("Asset {AssetId} processed ({Kind}, geotag {Source}).", asset.Id, asset.Kind, asset.GeotagSource);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AssetLifecycle.TryFailAttempt(asset, DateTimeOffset.UtcNow, Truncate(ex.Message), _opts.MaxProcessingAttempts);
                logger.LogWarning(ex, "Asset {AssetId} processing attempt {Attempt} failed (now {Status}).",
                    asset.Id, asset.Attempts, asset.Status);
            }

            session.Store(asset);
            await session.SaveChangesAsync(ct);
        }

        if (now - _lastJanitorRun >= JanitorEvery)
        {
            _lastJanitorRun = now;
            await ExpireStaleDeclaredAsync(session, store, now, ct);
        }
    }

    /// <summary>The exact check the declare-time surrogate can't make: identical bytes whose declared
    /// metadata differed. The newcomer keeps only the pointer — its objects are redundant.</summary>
    private static async Task<bool> TryDedupeAsync(IDocumentSession session, IObjectStore store, PhotoAsset asset, CancellationToken ct)
    {
        if (asset.Sha256 is not { } hash) return false;

        var canonical = await session.Query<PhotoAsset>()
            .Where(a => a.PrincipalId == asset.PrincipalId
                     && a.Id != asset.Id
                     && a.Sha256 == hash
                     && a.Status != AssetStatus.Duplicate)
            .OrderBy(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (canonical is null) return false;

        var thumbKey = asset.ThumbKey;
        if (!AssetLifecycle.TryMarkDuplicate(asset, canonical.Id, DateTimeOffset.UtcNow)) return false;

        await store.DeleteAsync(asset.OriginalKey, ct);
        if (thumbKey is not null) await store.DeleteAsync(thumbKey, ct);
        if (MetadataDonation.Apply(canonical, asset)) session.Store(canonical);
        return true;
    }

    private async Task ExpireStaleDeclaredAsync(IDocumentSession session, IObjectStore store, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now - TimeSpan.FromDays(_opts.DeclaredExpiryDays);
        var stale = await session.Query<PhotoAsset>()
            .Where(a => a.Status == AssetStatus.Declared && a.CreatedAt < cutoff)
            .Take(100)
            .ToListAsync(ct);
        if (stale.Count == 0) return;

        foreach (var asset in stale)
        {
            await store.DeleteAsync(asset.OriginalKey, ct);
            session.Delete<PhotoAsset>(asset.Id);
        }

        await session.SaveChangesAsync(ct);
        logger.LogInformation("Expired {Count} stale Declared assets.", stale.Count);
    }

    private static string Truncate(string message) => message.Length <= 500 ? message : message[..500];
}
