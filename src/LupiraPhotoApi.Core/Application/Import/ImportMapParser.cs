using System.Globalization;
using System.Text.RegularExpressions;

namespace LupiraPhotoApi.Core.Application.Import;

/// <summary>
/// Parses the import map. One entry per line, <c>#</c> comments:
/// <code>
/// camera  Sony G8341        = me
/// person  Bilder - Simon    = Simon Weideskog        (a name, a contact id, me, or none)
/// place   Ljungby           = @Skolgatan 18          (@entry, lat,lon, or a geocoder query)
/// place   Kungshamra 47     = Kungshamra 47, Solna | Kungshamra 47 ~ @Kungshamra 64A 500m
/// album   Student           = none                   (none, theme, event, or @other album)
/// prefer-filename 2016-03-11 Texas Furry Fiesta 2016
/// </code>
/// </summary>
public static partial class ImportMapParser
{
    public static ImportMap Parse(string text)
    {
        var map = new ImportMap();
        var lineNo = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNo++;
            var line = StripComment(raw).Trim();
            if (line.Length == 0) continue;

            var space = line.IndexOf(' ', StringComparison.Ordinal);
            var kind = space < 0 ? line : line[..space];
            var rest = space < 0 ? string.Empty : line[(space + 1)..].Trim();

            if (kind.Equals("prefer-filename", StringComparison.OrdinalIgnoreCase))
            {
                if (rest.Length == 0) map.Errors.Add($"line {lineNo}: prefer-filename needs an album or folder name");
                else map.PreferFilename.Add(rest);
                continue;
            }

            var eq = rest.IndexOf(" = ", StringComparison.Ordinal);
            if (eq < 0)
            {
                map.Errors.Add($"line {lineNo}: expected '<key> = <value>'");
                continue;
            }

            var key = Collapse(rest[..eq]);
            var value = rest[(eq + 3)..].Trim();
            switch (kind.ToLowerInvariant())
            {
                case "camera":
                    map.Cameras[key] = Person(value);
                    break;
                case "person":
                    map.People[key] = Person(value);
                    break;
                case "place":
                    if (Place(value) is { } place) map.Places[key] = place;
                    else map.Errors.Add($"line {lineNo}: unreadable place value '{value}'");
                    break;
                case "album":
                    if (Album(value) is { } album) map.Albums[key] = album;
                    else map.Errors.Add($"line {lineNo}: album value must be none, theme, event or @album");
                    break;
                default:
                    map.Errors.Add($"line {lineNo}: unknown entry '{kind}'");
                    break;
            }
        }

        return map;
    }

    /// <summary>Camera and folder names compare with runs of whitespace collapsed ("SAMSUNG     HMX200").</summary>
    public static string Collapse(string value) => Whitespace().Replace(value.Trim(), " ");

    private static string StripComment(string line)
    {
        var hash = line.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? line : line[..hash];
    }

    private static PersonRef Person(string value)
    {
        if (value.Equals("me", StringComparison.OrdinalIgnoreCase)) return new PersonRef { Kind = PersonRefKind.Me, Text = value };
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) return new PersonRef { Kind = PersonRefKind.None, Text = value };
        if (Guid.TryParse(value, out var id)) return new PersonRef { Kind = PersonRefKind.Contact, ContactId = id, Text = value };
        return new PersonRef { Kind = PersonRefKind.Unresolved, Text = value };
    }

    private static PlaceRule? Place(string value)
    {
        var rule = new PlaceRule();
        var tilde = value.IndexOf(" ~ ", StringComparison.Ordinal);
        if (tilde >= 0)
        {
            var check = CheckPattern().Match(value[(tilde + 3)..].Trim());
            if (!check.Success) return null;
            rule.CheckNear = Collapse(check.Groups["near"].Value);
            rule.CheckMeters = double.Parse(check.Groups["m"].Value, CultureInfo.InvariantCulture);
            value = value[..tilde].Trim();
        }

        var bar = value.IndexOf(" | ", StringComparison.Ordinal);
        if (bar >= 0)
        {
            rule.Label = value[(bar + 3)..].Trim();
            value = value[..bar].Trim();
        }

        if (value.StartsWith('@'))
            rule.SameAs = Collapse(value[1..]);
        else if (CoordinatePattern().Match(value) is { Success: true } c)
            (rule.Latitude, rule.Longitude) = (double.Parse(c.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(c.Groups[2].Value, CultureInfo.InvariantCulture));
        else if (value.Length > 0)
            rule.Query = value;
        else
            return null;
        return rule;
    }

    private static AlbumRule? Album(string value)
    {
        if (value.StartsWith('@')) return new AlbumRule { Kind = AlbumRuleKind.SameAs, SameAs = Collapse(value[1..]) };
        return value.ToLowerInvariant() switch
        {
            "none" => new AlbumRule { Kind = AlbumRuleKind.None },
            "theme" => new AlbumRule { Kind = AlbumRuleKind.Theme },
            "event" => new AlbumRule { Kind = AlbumRuleKind.Event },
            _ => null,
        };
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)$")]
    private static partial Regex CoordinatePattern();

    [GeneratedRegex(@"^@(?<near>.+?)\s+(?<m>\d+(?:\.\d+)?)\s*m$")]
    private static partial Regex CheckPattern();
}
