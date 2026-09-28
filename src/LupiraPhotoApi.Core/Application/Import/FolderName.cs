using System.Globalization;
using System.Text.RegularExpressions;

namespace LupiraPhotoApi.Core.Application.Import;

/// <summary><c>2017-10-03 Svalbard</c>, <c>2020-07 Evelina Segling</c>, <c>2007 Sommar</c>,
/// <c>Heden 2013-09-09</c>, or no date at all.</summary>
public static partial class FolderName
{
    public static FolderInfo Parse(string name)
    {
        var trimmed = name.Trim();
        if (Leading().Match(trimmed) is { Success: true } lead)
        {
            var (date, precision) = ToDate(lead.Groups["y"].Value, lead.Groups["m"].Value, lead.Groups["d"].Value);
            if (date is not null) return new FolderInfo { Name = trimmed, Title = lead.Groups["t"].Value.Trim(), Date = date, Precision = precision };
        }

        if (Trailing().Match(trimmed) is { Success: true } trail)
        {
            var (date, precision) = ToDate(trail.Groups["y"].Value, trail.Groups["m"].Value, trail.Groups["d"].Value);
            if (date is not null) return new FolderInfo { Name = trimmed, Title = trail.Groups["t"].Value.Trim(), Date = date, Precision = precision };
        }

        return new FolderInfo { Name = trimmed, Title = trimmed, Precision = FolderDatePrecision.None };
    }

    private static (DateOnly?, FolderDatePrecision) ToDate(string y, string m, string d)
    {
        var year = int.Parse(y, CultureInfo.InvariantCulture);
        if (year is < 1900 or > 2100) return (null, FolderDatePrecision.None);
        if (m.Length == 0) return (new DateOnly(year, 1, 1), FolderDatePrecision.Year);
        var month = int.Parse(m, CultureInfo.InvariantCulture);
        if (month is < 1 or > 12) return (null, FolderDatePrecision.None);
        if (d.Length == 0) return (new DateOnly(year, month, 1), FolderDatePrecision.Month);
        var day = int.Parse(d, CultureInfo.InvariantCulture);
        return day >= 1 && day <= DateTime.DaysInMonth(year, month)
            ? (new DateOnly(year, month, day), FolderDatePrecision.Day)
            : (null, FolderDatePrecision.None);
    }

    [GeneratedRegex(@"^(?<y>\d{4})(?:-(?<m>\d{2})(?:-(?<d>\d{2}))?)?(?:\s+(?<t>.*))?$")]
    private static partial Regex Leading();

    [GeneratedRegex(@"^(?<t>.+?)\s+(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})$")]
    private static partial Regex Trailing();
}
