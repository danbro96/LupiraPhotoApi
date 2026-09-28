using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ImageMagick;
using LupiraPhotoApi.Core.Application.Processing;
using LupiraPhotoApi.Core.Domain;

namespace LupiraPhotoApi.Media;

/// <summary>Stills: the EXIF profile via Magick.NET <c>Ping</c> (no pixel decode). Videos: ffprobe's container
/// tags — <c>creation_time</c> and the phone's ISO 6709 location. Unreadable metadata is empty, never an error.</summary>
public sealed partial class MediaMetadataReader(ILogger<MediaMetadataReader> logger) : IMediaMetadataReader
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    public async Task<MediaMetadata> ReadAsync(string path, AssetKind kind, CancellationToken ct = default)
    {
        try
        {
            return kind == AssetKind.Photo ? ReadExif(path) : await ReadVideoAsync(path, ct);
        }
        catch (Exception ex) when (ex is MagickException or IOException or InvalidOperationException or JsonException or Win32Exception)
        {
            logger.LogDebug(ex, "No readable metadata in {Path}.", path);
            return MediaMetadata.Empty;
        }
    }

    private static MediaMetadata ReadExif(string path)
    {
        using var image = new MagickImage();
        image.Ping(path);
        if (image.GetExifProfile() is not { } exif) return MediaMetadata.Empty;

        var metadata = new MediaMetadata();
        var taken = Text(exif.GetValue(ExifTag.DateTimeOriginal)?.Value)
            ?? Text(exif.GetValue(ExifTag.DateTimeDigitized)?.Value)
            ?? Text(exif.GetValue(ExifTag.DateTime)?.Value);
        if (taken is not null && DateTime.TryParseExact(taken[..Math.Min(19, taken.Length)], "yyyy:MM:dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            metadata.TakenAtLocal = local;
            if (Text(exif.GetValue(ExifTag.OffsetTimeOriginal)?.Value) is { } offset
                && TimeSpan.TryParse(offset.TrimStart('+'), CultureInfo.InvariantCulture, out var span))
                metadata.TakenAtOffset = span;
        }

        if (Coordinate(exif.GetValue(ExifTag.GPSLatitude)?.Value, Text(exif.GetValue(ExifTag.GPSLatitudeRef)?.Value)) is { } lat
            && Coordinate(exif.GetValue(ExifTag.GPSLongitude)?.Value, Text(exif.GetValue(ExifTag.GPSLongitudeRef)?.Value)) is { } lon
            && !(Math.Abs(lat) < 1e-6 && Math.Abs(lon) < 1e-6))
            (metadata.Latitude, metadata.Longitude) = (lat, lon);

        var camera = new CameraInfo
        {
            Make = Text(exif.GetValue(ExifTag.Make)?.Value),
            Model = Text(exif.GetValue(ExifTag.Model)?.Value),
            Lens = Text(exif.GetValue(ExifTag.LensModel)?.Value),
            FocalLengthMm = Number(exif.GetValue(ExifTag.FocalLength)?.Value),
            FNumber = Number(exif.GetValue(ExifTag.FNumber)?.Value),
            ExposureSeconds = Number(exif.GetValue(ExifTag.ExposureTime)?.Value),
            Iso = exif.GetValue(ExifTag.ISOSpeedRatings)?.Value is { Length: > 0 } iso ? iso[0] : null,
            Software = Text(exif.GetValue(ExifTag.Software)?.Value),
        };
        if (camera.Make is not null || camera.Model is not null) metadata.Camera = camera;
        return metadata;
    }

    private static async Task<MediaMetadata> ReadVideoAsync(string path, CancellationToken ct)
    {
        var psi = new ProcessStartInfo { FileName = "ffprobe", RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "quiet", "-print_format", "json", "-show_format", path })
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffprobe.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ProbeTimeout);
        var stdout = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0) return MediaMetadata.Empty;

        using var doc = JsonDocument.Parse(stdout);
        if (!doc.RootElement.TryGetProperty("format", out var format) || !format.TryGetProperty("tags", out var tags))
            return MediaMetadata.Empty;

        var metadata = new MediaMetadata();
        foreach (var tag in tags.EnumerateObject())
        {
            var value = tag.Value.GetString();
            if (value is null) continue;
            if (tag.Name.Equals("creation_time", StringComparison.OrdinalIgnoreCase)
                && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var created))
                metadata.TakenAtUtc = created;
            else if ((tag.Name.Equals("location", StringComparison.OrdinalIgnoreCase)
                      || tag.Name.EndsWith("location.ISO6709", StringComparison.OrdinalIgnoreCase))
                     && Iso6709().Match(value) is { Success: true } m)
                (metadata.Latitude, metadata.Longitude) =
                    (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        }

        return metadata;
    }

    /// <summary>EXIF ASCII fields are fixed-width and NUL-padded (Elephone writes "elephone\0\0…").</summary>
    private static string? Text(string? value)
    {
        var trimmed = value?.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static double? Number(Rational? value) =>
        value is { Denominator: not 0 } r ? r.ToDouble() : null;

    private static double? Coordinate(Rational[]? dms, string? reference)
    {
        if (dms is not { Length: 3 } || dms.Any(r => r.Denominator == 0)) return null;
        var degrees = dms[0].ToDouble() + (dms[1].ToDouble() / 60) + (dms[2].ToDouble() / 3600);
        return reference is "S" or "W" ? -degrees : degrees;
    }

    [GeneratedRegex(@"^([+-]\d+(?:\.\d+)?)([+-]\d+(?:\.\d+)?)")]
    private static partial Regex Iso6709();
}
