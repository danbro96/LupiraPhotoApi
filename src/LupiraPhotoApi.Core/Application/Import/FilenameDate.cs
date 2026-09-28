using System.Globalization;
using System.Text.RegularExpressions;

namespace LupiraPhotoApi.Core.Application.Import;

/// <summary>Capture dates written into file names by cameras and upload tools — <c>IMG_20190607_102209</c>,
/// <c>2016-03-02 15.43.00</c>, <c>DSCPDC_0001_BURST20190607122210548</c>, <c>… 2019-05-04</c>. Messenger
/// downloads (<c>photo_…</c>, WhatsApp <c>-WA</c>, <c>FB_IMG_</c>) name files by when they were sent, so
/// those dates are flagged as transfer dates rather than capture dates.</summary>
public static partial class FilenameDate
{
    public static bool TryParse(string fileName, out DateTime local, out bool isTransfer)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        isTransfer = TransferPattern().IsMatch(stem);
        foreach (Match m in DatePattern().Matches(stem))
        {
            var hasTime = m.Groups["H"].Success;
            if (!hasTime && m.Index + m.Length < stem.Length && char.IsDigit(stem[m.Index + m.Length])) continue;
            var text = hasTime
                ? $"{m.Groups["y"].Value}-{m.Groups["m"].Value}-{m.Groups["d"].Value} {m.Groups["H"].Value}:{m.Groups["M"].Value}:{m.Groups["S"].Value}"
                : $"{m.Groups["y"].Value}-{m.Groups["m"].Value}-{m.Groups["d"].Value} 12:00:00";
            if (DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out local))
                return true;
        }

        local = default;
        return false;
    }

    [GeneratedRegex(@"(?<!\d)(?<y>(?:19|20)\d{2})[-_.]?(?<m>0[1-9]|1[0-2])[-_.]?(?<d>0[1-9]|[12]\d|3[01])(?:[-_ T.]?(?<H>[01]\d|2[0-3])[-_.:]?(?<M>[0-5]\d)[-_.:]?(?<S>[0-5]\d))?")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"^(?:photo_|video_|FB_IMG_|received_|USER_SCOPED_TEMP_DATA_MSGR|Resized_)|-WA\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex TransferPattern();
}
