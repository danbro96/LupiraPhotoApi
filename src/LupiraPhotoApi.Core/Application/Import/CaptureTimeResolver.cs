using LupiraPhotoApi.Core.Domain;

namespace LupiraPhotoApi.Core.Application.Import;

/// <summary>Picks a capture time: EXIF → video container → file name → Takeout sidecar → folder date → album
/// core start → transfer date (messenger name, sidecar upload time) → file time. Implausible values are dropped
/// first, so a file-name date rescues a camera that lost its clock.</summary>
public static class CaptureTimeResolver
{
    /// <summary>A Takeout sidecar whose "taken" time sits this close to its upload time was dated by upload.</summary>
    public static readonly TimeSpan UploadEchoTolerance = TimeSpan.FromMinutes(5);

    public static CaptureDecision Resolve(CaptureCandidates c, TimeZoneInfo zone, DateTimeOffset now, bool preferFilename)
    {
        var rejected = new List<string>();
        DateTimeOffset? Check(DateTimeOffset? value, string label)
        {
            if (value is not { } v) return null;
            if (IsPlausible(v, now)) return v;
            rejected.Add($"{label} {v:yyyy-MM-dd} implausible");
            return null;
        }

        var exif = Check(c.ExifLocal is { } el ? Localize(el, c.ExifOffset, zone) : null, "exif");
        var video = Check(c.VideoUtc, "video");
        var filename = Check(c.FilenameLocal is { } fl ? Localize(fl, null, zone) : null, "filename");
        var captureName = c.FilenameIsTransfer ? null : filename;
        var sidecar = Check(c.SidecarTakenUtc, "sidecar");
        if (sidecar is { } st && c.SidecarUploadUtc is { } up && (st - up).Duration() <= UploadEchoTolerance)
            sidecar = null;

        (DateTimeOffset, DateTimeOffset)? disagreement = null;
        if (exif is { } e && captureName is { } f && (e - f).Duration() > TimeSpan.FromDays(1))
            disagreement = (e, f);

        var (takenAt, source) = (exif, video, captureName, sidecar) switch
        {
            ({ }, _, { } f2, _) when disagreement is not null && preferFilename => (f2, TakenAtSource.Filename),
            ({ } e2, _, _, _) => (e2, TakenAtSource.Exif),
            (_, { } v2, _, _) => (v2, TakenAtSource.Exif),
            (_, _, { } f2, _) => (f2, TakenAtSource.Filename),
            (_, _, _, { } s2) => (s2, TakenAtSource.Sidecar),
            _ => Fallback(c, zone, filename),
        };

        var decision = new CaptureDecision { TakenAt = takenAt.ToUniversalTime(), Source = source, Disagreement = disagreement };
        decision.Rejected.AddRange(rejected);
        return decision;
    }

    /// <summary>Camera-reset dates are checked on both the wall clock and UTC — a zoneless "2000-01-01 00:00"
    /// read in Stockholm is 31 December in UTC.</summary>
    public static bool IsPlausible(DateTimeOffset value, DateTimeOffset now)
    {
        static bool Reset(DateTime d) => d == new DateTime(1970, 1, 1) || d == new DateTime(2000, 1, 1);
        return value.Year >= 1995
            && value <= now.AddDays(1)
            && !Reset(value.Date)
            && !Reset(value.UtcDateTime.Date);
    }

    private static (DateTimeOffset, TakenAtSource) Fallback(CaptureCandidates c, TimeZoneInfo zone, DateTimeOffset? transferName)
    {
        if (c.FolderDate is { } folder && c.FolderPrecision is FolderDatePrecision.Day or FolderDatePrecision.Month)
            return (Noon(folder, zone), TakenAtSource.Folder);
        if (c.AlbumCoreStart is { } core)
            return (Noon(core, zone), TakenAtSource.Album);
        if (transferName is { } transfer)
            return (transfer, TakenAtSource.Upload);
        if (c.SidecarUploadUtc is { } upload)
            return (upload, TakenAtSource.Upload);
        return (c.FileTimeUtc ?? DateTimeOffset.UtcNow, TakenAtSource.FileTime);
    }

    private static DateTimeOffset Localize(DateTime local, TimeSpan? offset, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, offset ?? zone.GetUtcOffset(unspecified));
    }

    private static DateTimeOffset Noon(DateOnly date, TimeZoneInfo zone) =>
        Localize(date.ToDateTime(new TimeOnly(12, 0)), null, zone);
}
