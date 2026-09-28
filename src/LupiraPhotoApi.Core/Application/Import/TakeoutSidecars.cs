using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LupiraPhotoApi.Core.Application.Import;

/// <summary>
/// Finds and reads a Takeout file's JSON sidecar. Google names it <c>&lt;file&gt;.supplemental-metadata.json</c>,
/// truncates the whole name when it runs long (<c>&lt;file&gt;.supplemental-metad.json</c>), and moves a
/// duplicate counter to the end (<c>IMG_1(1).jpg</c> → <c>IMG_1.jpg.supplemental-metadata(1).json</c>).
/// </summary>
public static partial class TakeoutSidecars
{
    private const string Suffix = ".supplemental-metadata";

    public static bool IsSidecar(string fileName) =>
        fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns the sidecar's file name from <paramref name="siblings"/>, or null.</summary>
    public static string? Find(string fileName, IReadOnlySet<string> siblings)
    {
        if (Match(fileName, string.Empty, siblings) is { } direct) return direct;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (Counter().Match(stem) is not { Success: true } m) return null;
        var name = stem[..m.Index].TrimEnd() + Path.GetExtension(fileName);
        return Match(name, m.Groups[1].Value, siblings);
    }

    private static string? Match(string name, string counter, IReadOnlySet<string> siblings)
    {
        var full = name + Suffix;
        foreach (var candidate in siblings)
        {
            if (!IsSidecar(candidate)) continue;
            var body = candidate[..^".json".Length];
            if (counter.Length > 0)
            {
                if (!body.EndsWith(counter, StringComparison.Ordinal)) continue;
                body = body[..^counter.Length];
            }

            if (body.Equals(name, StringComparison.Ordinal)) return candidate;
            if (body.Length > name.Length && full.StartsWith(body, StringComparison.Ordinal)) return candidate;
        }

        return null;
    }

    public static TakeoutSidecar? Read(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var sidecar = new TakeoutSidecar
            {
                TakenUtc = Timestamp(root, "photoTakenTime"),
                UploadUtc = Timestamp(root, "creationTime"),
                FromSharedAlbum = root.TryGetProperty("googlePhotosOrigin", out var origin) && origin.TryGetProperty("fromSharedAlbum", out _),
            };
            if (root.TryGetProperty("geoData", out var geo)
                && geo.TryGetProperty("latitude", out var lat) && geo.TryGetProperty("longitude", out var lon)
                && (Math.Abs(lat.GetDouble()) > 1e-6 || Math.Abs(lon.GetDouble()) > 1e-6))
                (sidecar.Latitude, sidecar.Longitude) = (lat.GetDouble(), lon.GetDouble());
            return sidecar;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static DateTimeOffset? Timestamp(JsonElement root, string property) =>
        root.TryGetProperty(property, out var node)
        && node.TryGetProperty("timestamp", out var ts)
        && long.TryParse(ts.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    [GeneratedRegex(@"(\(\d+\))$")]
    private static partial Regex Counter();
}
