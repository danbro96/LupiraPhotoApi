using LupiraPhotoApi.Core.Application;
using Microsoft.Extensions.Options;

namespace LupiraPhotoApi.Workers;

/// <summary>Hourly sweep that permanently deletes assets trashed longer than the retention, through the
/// same purge path as <c>DELETE /photos/{id}</c>. One bad sweep never kills the loop.</summary>
public sealed class TrashPurgeWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<PhotoOptions> options,
    ILogger<TrashPurgeWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepEvery = TimeSpan.FromHours(1);

    private readonly PhotoOptions _opts = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(2, 8)), stoppingToken);

        using var timer = new PeriodicTimer(SweepEvery);
        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Trash purge sweep failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var deletes = scope.ServiceProvider.GetRequiredService<PhotoDeleteService>();
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(_opts.TrashRetentionDays);

        var purged = await deletes.PurgeTrashedBeforeAsync(cutoff, ct);
        if (purged > 0) logger.LogInformation("Purged {Count} assets past their trash retention.", purged);
    }
}
