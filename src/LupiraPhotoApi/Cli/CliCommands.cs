using LupiraPhotoApi.Core.Application;
using LupiraPhotoApi.Core.Application.Import;

namespace LupiraPhotoApi.Cli;

/// <summary>
/// One-shot commands run inside the API container (<c>docker exec … dotnet LupiraPhotoApi.dll …</c>):
/// <code>
/// --import &lt;dir&gt; --source handelser|platser|family|takeout --principal &lt;email&gt;
///     [--me-contact &lt;id&gt;] [--map &lt;file&gt;] [--timezone Europe/Stockholm] [--dry-run]
/// --backfill-captured-by --principal &lt;email&gt; --contact &lt;id&gt;
/// --reprocess-all --principal &lt;email&gt;
/// </code>
/// The running API's worker processes what they queue; these commands only declare, upload and flag.
/// </summary>
public static class CliCommands
{
    public static bool Handles(string[] args) =>
        args.Contains("--import") || args.Contains("--backfill-captured-by") || args.Contains("--reprocess-all");

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var ct = CancellationToken.None;

        if (Value(args, "--principal") is not { } email)
            return Fail("--principal <email> is required.");
        if (await sp.GetRequiredService<PrincipalDirectory>().FindByEmailAsync(email, ct) is not { } principal)
            return Fail($"No principal for {email} — sign in once so it is provisioned.");

        if (args.Contains("--backfill-captured-by"))
        {
            if (!Guid.TryParse(Value(args, "--contact"), out var contact)) return Fail("--contact <id> is required.");
            var n = await sp.GetRequiredService<PhotoMaintenanceService>().BackfillCapturedByAsync(principal.Id, contact, ct);
            Console.WriteLine($"Photographer set on {n} phone uploads.");
            return 0;
        }

        if (args.Contains("--reprocess-all"))
        {
            var n = await sp.GetRequiredService<PhotoMaintenanceService>().ReprocessAllAsync(principal.Id, ct);
            Console.WriteLine($"Queued {n} assets for reprocessing.");
            return 0;
        }

        if (Value(args, "--import") is not { } root || !Directory.Exists(root))
            return Fail("--import <dir> must name an existing directory.");
        if (!Enum.TryParse<ImportSource>(Value(args, "--source"), ignoreCase: true, out var source))
            return Fail("--source must be handelser, platser, family or takeout.");

        var mapPath = Value(args, "--map");
        var map = mapPath is null ? new ImportMap() : ImportMapParser.Parse(await File.ReadAllTextAsync(mapPath, ct));
        Guid? me = Guid.TryParse(Value(args, "--me-contact"), out var meId) ? meId : null;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Value(args, "--timezone") ?? "Europe/Stockholm");

        var report = await sp.GetRequiredService<PhotoImporter>().RunAsync(new ImportRequest
        {
            Root = root,
            Source = source,
            PrincipalId = principal.Id,
            MeContactId = me,
            Map = map,
            DryRun = args.Contains("--dry-run"),
            Zone = zone,
        }, ct);
        Console.WriteLine(report.ToText());
        return report.Errors.Count == 0 ? 0 : 1;
    }

    private static string? Value(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : null;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }
}
