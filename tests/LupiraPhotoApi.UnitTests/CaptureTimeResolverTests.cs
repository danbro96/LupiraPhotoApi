using LupiraPhotoApi.Core.Application.Import;
using LupiraPhotoApi.Core.Domain;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class CaptureTimeResolverTests
{
    private static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private static CaptureDecision Resolve(CaptureCandidates c, bool preferFilename = false) =>
        CaptureTimeResolver.Resolve(c, Stockholm, Now, preferFilename);

    [Fact]
    public void Exif_IsReadInTheLocalZone()
    {
        var d = Resolve(new CaptureCandidates { ExifLocal = new DateTime(2019, 6, 7, 10, 0, 0) });
        Assert.Equal(TakenAtSource.Exif, d.Source);
        Assert.Equal(new DateTimeOffset(2019, 6, 7, 8, 0, 0, TimeSpan.Zero), d.TakenAt.ToUniversalTime());
    }

    [Fact]
    public void ImplausibleExif_IsRescuedByTheFileName()
    {
        var d = Resolve(new CaptureCandidates
        {
            ExifLocal = new DateTime(2000, 1, 1, 0, 0, 0),
            FilenameLocal = new DateTime(2017, 10, 5, 10, 8, 3),
        });
        Assert.Equal(TakenAtSource.Filename, d.Source);
        Assert.Equal(2017, d.TakenAt.Year);
        Assert.Single(d.Rejected);
    }

    [Fact]
    public void UploadEchoingSidecar_FallsBackToTheAlbum()
    {
        var upload = new DateTimeOffset(2019, 12, 17, 18, 0, 0, TimeSpan.Zero);
        var d = Resolve(new CaptureCandidates
        {
            SidecarTakenUtc = upload.AddSeconds(2),
            SidecarUploadUtc = upload,
            AlbumCoreStart = new DateOnly(2019, 6, 6),
        });
        Assert.Equal(TakenAtSource.Album, d.Source);
        Assert.Equal(new DateOnly(2019, 6, 6), DateOnly.FromDateTime(d.TakenAt.Date));
    }

    [Fact]
    public void FolderDate_BeatsAlbumCoreAndTransferNames()
    {
        var d = Resolve(new CaptureCandidates
        {
            FilenameLocal = new DateTime(2016, 3, 22, 14, 57, 54),
            FilenameIsTransfer = true,
            FolderDate = new DateOnly(2016, 3, 11),
            FolderPrecision = FolderDatePrecision.Day,
            AlbumCoreStart = new DateOnly(2016, 3, 10),
        });
        Assert.Equal(TakenAtSource.Folder, d.Source);
        Assert.Equal(11, d.TakenAt.Day);
    }

    [Fact]
    public void TransferName_IsOnlyAnApproximateUploadDate()
    {
        var d = Resolve(new CaptureCandidates { FilenameLocal = new DateTime(2016, 3, 22, 14, 57, 54), FilenameIsTransfer = true });
        Assert.Equal(TakenAtSource.Upload, d.Source);
    }

    [Fact]
    public void ExifAndNameDisagreeing_ExifWinsUnlessFilenameIsPreferred()
    {
        var c = new CaptureCandidates { ExifLocal = new DateTime(2018, 3, 23, 9, 0, 0), FilenameLocal = new DateTime(2018, 4, 23, 9, 0, 0) };
        var byExif = Resolve(c);
        var byName = Resolve(c, preferFilename: true);

        Assert.NotNull(byExif.Disagreement);
        Assert.Equal((TakenAtSource.Exif, 3), (byExif.Source, byExif.TakenAt.Month));
        Assert.Equal((TakenAtSource.Filename, 4), (byName.Source, byName.TakenAt.Month));
    }

    [Fact]
    public void NothingAtAll_UsesTheFileTime()
    {
        var mtime = new DateTimeOffset(2021, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var d = Resolve(new CaptureCandidates { FileTimeUtc = mtime });
        Assert.Equal((TakenAtSource.FileTime, mtime), (d.Source, d.TakenAt));
    }

    [Fact]
    public void CameraClock_InUtc_ReadsExifAsUtc()
    {
        var d = Resolve(new CaptureCandidates { ExifLocal = new DateTime(2015, 7, 1, 10, 0, 0), CameraClock = TimeZoneInfo.Utc });
        Assert.Equal(new DateTimeOffset(2015, 7, 1, 10, 0, 0, TimeSpan.Zero), d.TakenAt);
    }

    [Fact]
    public void CameraClock_AtAFixedOffset_IgnoresSummerTime()
    {
        var winter = ImportMapParser.Parse("clock HTC Desire = +01:00").Clocks["HTC Desire"];
        var d = Resolve(new CaptureCandidates { ExifLocal = new DateTime(2011, 7, 1, 10, 0, 0), CameraClock = winter });
        Assert.Equal(new DateTimeOffset(2011, 7, 1, 9, 0, 0, TimeSpan.Zero), d.TakenAt);
    }

    [Fact]
    public void ExifOffset_BeatsTheCameraClock()
    {
        var d = Resolve(new CaptureCandidates
        {
            ExifLocal = new DateTime(2019, 6, 7, 10, 0, 0),
            ExifOffset = TimeSpan.FromHours(2),
            CameraClock = TimeZoneInfo.Utc,
        });
        Assert.Equal(new DateTimeOffset(2019, 6, 7, 8, 0, 0, TimeSpan.Zero), d.TakenAt);
    }
}
