using System.Text;
using LupiraPhotoApi.Core.Domain;

namespace LupiraPhotoApi.Core.Application.Import;

/// <summary>What an import did — or, on a dry run, would do.</summary>
public sealed class ImportReport
{
    public bool DryRun { get; set; }

    public int Files { get; set; }

    public int Imported { get; set; }

    public int AlreadyPresent { get; set; }

    public int Duplicates { get; set; }

    public Dictionary<string, int> ByKind { get; } = [];

    public Dictionary<string, int> Unsupported { get; } = [];

    public int Skipped { get; set; }

    public Dictionary<TakenAtSource, int> CaptureSources { get; } = [];

    public List<string> RejectedDates { get; } = [];

    public List<string> Disagreements { get; } = [];

    public Dictionary<string, int> Cameras { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> UnmappedCameras { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> Photographers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> UnmappedSubfolders { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Places { get; } = [];

    public List<string> Albums { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> Errors { get; } = [];

    public void Count<TKey>(Dictionary<TKey, int> counter, TKey key)
        where TKey : notnull => counter[key] = counter.GetValueOrDefault(key) + 1;

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(DryRun ? "== DRY RUN — nothing written ==" : "== Import ==");
        sb.AppendLine($"files {Files}  imported {Imported}  already present {AlreadyPresent}  duplicates {Duplicates}  skipped {Skipped}");
        Section(sb, "by kind", ByKind.Select(kv => $"{kv.Key}: {kv.Value}"));
        Section(sb, "unsupported", Unsupported.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}: {kv.Value}"));
        Section(sb, "capture time from", CaptureSources.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}: {kv.Value}"));
        Section(sb, "cameras", Cameras.OrderByDescending(kv => kv.Value)
            .Select(kv => $"{kv.Key}: {kv.Value}{(UnmappedCameras.Contains(kv.Key) ? "   (unmapped)" : string.Empty)}"));
        Section(sb, "photographers", Photographers.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}: {kv.Value}"));
        Section(sb, "unmapped subfolders", UnmappedSubfolders.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}: {kv.Value}"));
        Section(sb, "places", Places);
        Section(sb, "albums", Albums);
        Section(sb, "EXIF vs file name > 1 day", Disagreements);
        Section(sb, "rejected dates", RejectedDates);
        Section(sb, "warnings", Warnings);
        Section(sb, "ERRORS", Errors);
        return sb.ToString();
    }

    private static void Section(StringBuilder sb, string title, IEnumerable<string> lines)
    {
        var list = lines.ToList();
        if (list.Count == 0) return;
        sb.AppendLine().AppendLine($"-- {title} ({list.Count})");
        foreach (var line in list) sb.AppendLine($"  {line}");
    }
}
