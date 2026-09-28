using System.Globalization;
using LupiraPhotoApi.Core.Domain;

namespace LupiraPhotoApi.Core.Application.Import;

/// <summary>Resolves a place name to a folder hint, in order: its map entry (another entry, coordinates, or a
/// query) → the folder's own agreed GPS → a gazetteer/geocoder lookup of the name. Results are cached per run
/// and described for the dry run; an unresolved name still yields a label-only hint.</summary>
internal sealed class PlaceRules(ImportMap map, IPlaceResolver resolver, ImportReport report)
{
    private const int MaxDepth = 5;

    private readonly Dictionary<string, (double Latitude, double Longitude, string Note)> _ownGps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Resolution> _cache = new(StringComparer.OrdinalIgnoreCase);

    public void AddOwnGps(string key, (double Latitude, double Longitude)? centroid, int gpsCount, int fileCount)
    {
        if (centroid is { } c) _ownGps[key] = (c.Latitude, c.Longitude, $"own GPS {gpsCount}/{fileCount}");
    }

    public async Task<PlaceHint> ResolveAsync(string key, CancellationToken ct)
    {
        var r = await ResolveCoreAsync(key, 0, ct);
        return new PlaceHint { Source = PlaceHintSource.Folder, Latitude = r.Latitude, Longitude = r.Longitude, Label = r.Label };
    }

    public IEnumerable<string> Describe() => _cache.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv =>
        kv.Value.Latitude is { } lat && kv.Value.Longitude is { } lon
            ? $"{kv.Key} → {lat.ToString("0.0000", CultureInfo.InvariantCulture)},{lon.ToString("0.0000", CultureInfo.InvariantCulture)} \"{kv.Value.Label}\" ({kv.Value.How})"
            : $"{kv.Key} → label only \"{kv.Value.Label}\" ({kv.Value.How})");

    private async Task<Resolution> ResolveCoreAsync(string key, int depth, CancellationToken ct)
    {
        if (_cache.TryGetValue(key, out var cached)) return cached;
        if (depth > MaxDepth)
        {
            report.Errors.Add($"place '{key}': @-references loop");
            return new Resolution(null, null, key, "loop");
        }

        Resolution result;
        if (map.Places.TryGetValue(key, out var rule))
        {
            if (rule.SameAs is { } target)
            {
                var t = await ResolveCoreAsync(target, depth + 1, ct);
                result = new Resolution(t.Latitude, t.Longitude, rule.Label ?? t.Label, $"same as {target}");
            }
            else if (rule is { Latitude: { } lat, Longitude: { } lon })
            {
                result = new Resolution(lat, lon, rule.Label ?? key, "map coordinates");
            }
            else
            {
                var hit = await resolver.ResolveAsync(rule.Query!, ct);
                result = hit is null
                    ? new Resolution(null, null, rule.Label ?? key, $"'{rule.Query}' not found")
                    : new Resolution(hit.Latitude, hit.Longitude, rule.Label ?? key, $"'{rule.Query}' via {hit.Via}");
            }

            if (rule.CheckNear is { } near && rule.CheckMeters is { } meters && result is { Latitude: { } rl, Longitude: { } rn })
            {
                var anchor = await ResolveCoreAsync(near, depth + 1, ct);
                if (anchor is { Latitude: { } al, Longitude: { } an } && GeoMath.DistanceMeters(rl, rn, al, an) is var d && d > meters)
                    report.Warnings.Add($"place '{key}' resolved {d:0} m from '{near}' (expected ≤ {meters:0} m)");
            }
        }
        else if (_ownGps.TryGetValue(key, out var own))
        {
            result = new Resolution(own.Latitude, own.Longitude, key, own.Note);
        }
        else
        {
            var hit = await resolver.ResolveAsync(key, ct);
            result = hit is null
                ? new Resolution(null, null, key, "not found")
                : new Resolution(hit.Latitude, hit.Longitude, key, $"via {hit.Via}");
        }

        _cache[key] = result;
        return result;
    }

    private sealed record Resolution(double? Latitude, double? Longitude, string Label, string How);
}
