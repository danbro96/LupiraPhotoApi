using System.Net.Http.Json;
using ImageMagick;
using LupiraPhotoApi.Core.Application;
using LupiraPhotoApi.Core.Application.Import;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupiraPhotoApi.IntegrationTests;

/// <summary>The folder importer end to end: dates, albums, photographers, places, dedup and donation.</summary>
public class ImportTests(PhotoApiTestFactory factory) : IntegrationTest(factory), IDisposable
{
    private const string Email = "anna@example.com";
    private static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    private readonly string _root = Directory.CreateTempSubdirectory("lupira-import-").FullName;
    private readonly Guid _me = Guid.NewGuid();
    private readonly Guid _svante = Guid.NewGuid();
    private int _variant;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task EventFolders_CarryAlbumDatePhotographerAndPlace()
    {
        Write("2017-10-03 Svalbard/IMG_20171005_100000.jpg", Jpeg(exifTaken: "2017:10:05 10:00:00", make: "elephone", model: "Elephone P8000"));
        Write("2017-10-03 Svalbard/Svante/20171004_120000.jpg", Jpeg(exifTaken: "2017:10:04 12:00:00", make: "samsung", model: "SM-G930F"));
        Write("2017-10-03 Svalbard/received_1463499603728637.jpeg", Jpeg());
        Write("2021-06-26 Ljungby/DSC_0571.JPG", Jpeg(exifTaken: "2021:06:26 14:00:00"));
        Write("2021-06-26 Ljungby/Thumbs.db", [1, 2, 3]);
        Write("2021-06-26 Ljungby/notes.zip", [1, 2, 3]);
        var map = $"""
            camera Elephone P8000 = me
            person Svante         = {_svante}
            place  Skolgatan 18   = 56.8331,13.9410
            place  Ljungby        = @Skolgatan 18
            """;

        var report = await ImportAsync(ImportSource.Handelser, map);

        Assert.Empty(report.Errors);
        Assert.Equal((4, 4, 2), (report.Files, report.Imported, report.Skipped));
        var assets = await AssetsAsync();

        var elephone = assets.Single(a => a.MediaStoreId.EndsWith("IMG_20171005_100000.jpg", StringComparison.Ordinal));
        Assert.Equal(("2017-10-03 Svalbard", AlbumKind.Event, new DateOnly(2017, 10, 3)), (elephone.SourceAlbum, elephone.SourceAlbumKind!.Value, elephone.SourceAlbumDate!.Value));
        Assert.Equal((_me, CapturedBySource.CameraOwner), (elephone.CapturedByContactId!.Value, elephone.CapturedBySource!.Value));
        Assert.Equal(TakenAtSource.Exif, elephone.TakenAtSource);

        var svante = assets.Single(a => a.MediaStoreId.Contains("/Svante/", StringComparison.Ordinal));
        Assert.Equal((_svante, CapturedBySource.Folder), (svante.CapturedByContactId!.Value, svante.CapturedBySource!.Value));

        var received = assets.Single(a => a.MediaStoreId.EndsWith(".jpeg", StringComparison.Ordinal));
        Assert.Equal((TakenAtSource.Folder, new DateOnly(2017, 10, 3)), (received.TakenAtSource, DateOnly.FromDateTime(received.TakenAt.Date)));

        var ljungby = assets.Single(a => a.SourceAlbum == "2021-06-26 Ljungby");
        Assert.Equal((PlaceHintSource.Folder, 56.8331, "Skolgatan 18"), (ljungby.PlaceHint!.Source, ljungby.PlaceHint.Latitude!.Value, ljungby.PlaceHint.Label!));

        var api = Factory.ApiClient(Email);
        var ready = await WaitForStatusAsync(api, ljungby.Id, AssetStatus.Ready);
        Assert.Equal((GeotagSource.Folder, "Skolgatan 18"), (ready.GeotagSource, ready.PlaceLabel!));
        var camera = await WaitForStatusAsync(api, elephone.Id, AssetStatus.Ready);
        Assert.Equal("Elephone P8000", camera.Camera?.Model);
    }

    [Fact]
    public async Task ReRunning_IsIdempotent_AndAppliesACorrectedMap()
    {
        Write("2017-10-03 Svalbard/Svante/20171004_120000.jpg", Jpeg(exifTaken: "2017:10:04 12:00:00"));
        await ImportAsync(ImportSource.Handelser, map: string.Empty);

        var second = await ImportAsync(ImportSource.Handelser, $"person Svante = {_svante}");

        Assert.Equal((0, 1), (second.Imported, second.AlreadyPresent));
        Assert.Equal(_svante, (await AssetsAsync()).Single().CapturedByContactId);
    }

    [Fact]
    public async Task ARetime_SurvivesReRuns_AndTheFileStillFindsItsCopies()
    {
        var bytes = Jpeg(exifTaken: "2011:07:01 14:00:00", make: "HTC", model: "HTC Desire");
        Write("2011-07-01 Midsommar/IMAG0001.jpg", bytes);
        await ImportAsync(ImportSource.Handelser, map: string.Empty);
        var imported = (await AssetsAsync()).Single();
        var api = Factory.ApiClient(Email);
        await WaitForStatusAsync(api, imported.Id, AssetStatus.Ready);

        var retime = await api.PostAsJsonAsync("/photos/retime", new RetimePhotosRequest { Ids = [imported.Id], ShiftBy = TimeSpan.FromHours(-1) }, Json);
        Assert.True(retime.IsSuccessStatusCode, await retime.Content.ReadAsStringAsync());
        var manual = imported.TakenAt.AddHours(-1);

        var rerun = await ImportAsync(ImportSource.Handelser, "clock HTC Desire = +01:00");

        Assert.Equal((0, 1, 0), (rerun.Imported, rerun.AlreadyPresent, rerun.Duplicates));
        var kept = (await AssetsAsync()).Single();
        Assert.Equal((TakenAtSource.Manual, manual), (kept.TakenAtSource, kept.TakenAt));
        var derived = new DateTimeOffset(2011, 7, 1, 13, 0, 0, TimeSpan.Zero);
        Assert.Equal((derived, TakenAtSource.Exif), (kept.CaptureTimeOverride!.DerivedTakenAt, kept.CaptureTimeOverride.DerivedSource));

        var phone = PhotoDeclare(bytes, mediaStoreId: "phone-1");
        phone.TakenAt = derived;
        Assert.Equal(AssetStatus.Duplicate, (await DeclareAsync(api, phone)).Status);
    }

    [Fact]
    public async Task ClockLines_ReadACamerasExifInItsZone()
    {
        Write("2015-07-01 Sommar/a.jpg", Jpeg(exifTaken: "2015:07:01 10:00:00", make: "LGE", model: "Nexus 5"));
        Write("2015-07-01 Sommar/b.jpg", Jpeg(exifTaken: "2015:07:01 10:00:00", make: "Sony", model: "G8341"));

        await ImportAsync(ImportSource.Handelser, "clock Nexus 5 = utc");

        var assets = await AssetsAsync();
        Assert.Equal(new DateTimeOffset(2015, 7, 1, 10, 0, 0, TimeSpan.Zero), assets.Single(a => a.MediaStoreId.EndsWith("a.jpg", StringComparison.Ordinal)).TakenAt);
        Assert.Equal(new DateTimeOffset(2015, 7, 1, 8, 0, 0, TimeSpan.Zero), assets.Single(a => a.MediaStoreId.EndsWith("b.jpg", StringComparison.Ordinal)).TakenAt);
    }

    [Fact]
    public async Task ACopyOfAPhoneBackup_DonatesItsAlbumAndPhotographer()
    {
        var bytes = Jpeg(exifTaken: "2017:10:04 12:00:00");
        var api = Factory.ApiClient(Email);
        var phone = PhotoDeclare(bytes, mediaStoreId: "phone-1");
        phone.TakenAt = new DateTimeOffset(new DateTime(2017, 10, 4, 12, 0, 0), Stockholm.GetUtcOffset(new DateTime(2017, 10, 4, 12, 0, 0)));
        var canonical = await UploadFlowAsync(api, phone, bytes);
        await WaitForStatusAsync(api, canonical, AssetStatus.Ready);
        Write("2017-10-03 Svalbard/Svante/20171004_120000.jpg", bytes);

        var report = await ImportAsync(ImportSource.Handelser, $"person Svante = {_svante}");

        Assert.Equal(1, report.Duplicates);
        var phoneAsset = await api.GetFromJsonAsync<PhotoAssetDto>($"/photos/{canonical}", Json);
        Assert.Equal(("2017-10-03 Svalbard", _svante), (phoneAsset!.SourceAlbum, phoneAsset.CapturedByContactId!.Value));
    }

    [Fact]
    public async Task Takeout_RestoresTitles_AndDatesUploadEchoesFromTheAlbum()
    {
        Write("user-generated-memory-titles.json", """{"title":["\"Esquadern\" 2020"]}"""u8.ToArray());
        Write("_Esquadern_ 2020/DSC_1001.JPG", Jpeg(exifTaken: "2020:06:05 12:00:00"));
        Write("_Esquadern_ 2020/DSC_1001.JPG.supplemental-metadata.json", Sidecar(taken: 1591358400, upload: 1591358400));
        Write("_Esquadern_ 2020/62165882_1015876_n.jpg", Jpeg());
        Write("_Esquadern_ 2020/62165882_1015876_n.jpg.supplemental-metadata.json", Sidecar(taken: 1608220800, upload: 1608220800));
        Write("Photos from 2019/IMG_20190101_120000.jpg", Jpeg());
        Write("Photos from 2019/IMG_20190101_120000.jpg.supplemental-metadata.json", Sidecar(taken: 1546340400, upload: 1546400000));

        var report = await ImportAsync(ImportSource.Takeout, map: string.Empty);

        Assert.Equal(3, report.Imported);
        var assets = await AssetsAsync();
        var echo = assets.Single(a => a.MediaStoreId.EndsWith("_n.jpg", StringComparison.Ordinal));
        Assert.Equal(("\"Esquadern\" 2020", TakenAtSource.Album, new DateOnly(2020, 6, 5)), (echo.SourceAlbum, echo.TakenAtSource, DateOnly.FromDateTime(echo.TakenAt.Date)));
        Assert.Null(assets.Single(a => a.MediaStoreId.StartsWith("Photos from", StringComparison.Ordinal)).SourceAlbum);
    }

    [Fact]
    public async Task Takeout_ReEncodedCopies_CollapseOntoTheOriginal_AndFoldTheirAlbum()
    {
        Write("2016-08-01 Fjällvandring Sarek/P1010746.JPG", Jpeg(exifTaken: "2016:08:03 13:31:08", make: "Panasonic", model: "DMC-TZ60"));
        Write("2016-08-01 Fjällvandring Sarek/P1010747.JPG", Jpeg(exifTaken: "2016:08:03 13:40:00", make: "Panasonic", model: "DMC-TZ60"));
        await ImportAsync(ImportSource.Handelser, map: string.Empty);
        var originals = await AssetsAsync();
        Directory.Delete(Path.Combine(_root, "2016-08-01 Fjällvandring Sarek"), recursive: true);

        Write("Vandring i sarek/P1010746.JPG", Jpeg(exifTaken: "2016:08:03 13:31:08"));
        Write("Vandring i sarek/P1010747(1).JPG", Jpeg(exifTaken: "2016:08:03 13:40:00"));
        Write("Photos from 2021/P1010746.JPG", Jpeg(exifTaken: "2021:07:19 10:00:00"));

        var report = await ImportAsync(ImportSource.Takeout, map: string.Empty);

        Assert.Equal((2, 2, 1), (report.Duplicates, report.NearCopies, report.Imported));
        Assert.Contains(report.Albums, a => a.Contains("folded into \"2016-08-01 Fjällvandring Sarek\"", StringComparison.Ordinal));
        var copies = (await AssetsAsync()).Where(a => a.DeviceId == "import:takeout" && a.Status == AssetStatus.Duplicate).ToList();
        Assert.Equal(originals.Select(o => o.Id).Order(), copies.Select(c => c.DuplicateOfId!.Value).Order());
    }

    [Fact]
    public async Task PlaceFolders_LendTheirOwnGpsToPhotosWithout()
    {
        Write("Armégatan 32B/a.jpg", Jpeg(exifTaken: "2018:01:01 12:00:00", lat: 59.3500, lon: 18.0036));
        Write("Armégatan 32B/b.jpg", Jpeg(exifTaken: "2018:01:02 12:00:00", lat: 59.3502, lon: 18.0030));
        Write("Armégatan 32B/c.jpg", Jpeg(exifTaken: "2018:01:03 12:00:00", lat: 59.3499, lon: 18.0040));
        Write("Armégatan 32B/d.jpg", Jpeg(exifTaken: "2018:01:04 12:00:00"));

        await ImportAsync(ImportSource.Platser, map: string.Empty);

        var d = (await AssetsAsync()).Single(a => a.MediaStoreId.EndsWith("d.jpg", StringComparison.Ordinal));
        Assert.Equal("Armégatan 32B", d.PlaceHint!.Label);
        Assert.InRange(d.PlaceHint.Latitude!.Value, 59.349, 59.351);
    }

    [Fact]
    public async Task DryRun_WritesNothing_AndFlagsUnresolvedPeople()
    {
        Write("2017-10-03 Svalbard/Svante/20171004_120000.jpg", Jpeg(exifTaken: "2017:10:04 12:00:00"));
        Write("2017-10-03 Svalbard/Oscar/20171004_130000.jpg", Jpeg(exifTaken: "2017:10:04 13:00:00"));

        var report = await ImportAsync(ImportSource.Handelser, "person Svante = Svante Rollenhagen", dryRun: true);

        Assert.Empty(await AssetsAsync());
        Assert.Contains(report.Errors, e => e.Contains("Svante Rollenhagen", StringComparison.Ordinal));
        Assert.True(report.UnmappedSubfolders.ContainsKey("2017-10-03 Svalbard/Oscar"));
    }

    [Fact]
    public async Task Albums_SummariseTheCoreSpan()
    {
        Write("2019-06-06 Esquadern/a.jpg", Jpeg(exifTaken: "2019:06:06 12:00:00"));
        Write("2019-06-06 Esquadern/b.jpg", Jpeg(exifTaken: "2019:06:08 12:00:00"));
        Write("2019-06-06 Esquadern/c.jpg", Jpeg(exifTaken: "2019:12:17 12:00:00"));
        await ImportAsync(ImportSource.Handelser, map: string.Empty);

        var albums = await Factory.ApiClient(Email).GetFromJsonAsync<List<PhotoAlbumDto>>("/photos/albums", Json);

        var album = Assert.Single(albums!);
        Assert.Equal((new DateOnly(2019, 6, 6), new DateOnly(2019, 6, 8), 1, "handelser"), (album.CoreFrom!.Value, album.CoreTo!.Value, album.Outliers, album.Source!));
    }

    private async Task<ImportReport> ImportAsync(ImportSource source, string map, bool dryRun = false)
    {
        await Factory.ApiClient(Email).GetAsync("/me");
        using var scope = Factory.Services.CreateScope();
        var principal = await scope.ServiceProvider.GetRequiredService<PrincipalDirectory>().FindByEmailAsync(Email);
        return await scope.ServiceProvider.GetRequiredService<PhotoImporter>().RunAsync(new ImportRequest
        {
            Root = _root,
            Source = source,
            PrincipalId = principal!.Id,
            MeContactId = _me,
            Map = ImportMapParser.Parse(map),
            DryRun = dryRun,
            Zone = Stockholm,
        }, CancellationToken.None);
    }

    private async Task<List<PhotoAsset>> AssetsAsync()
    {
        await using var session = Factory.Store.QuerySession();
        return (await session.Query<PhotoAsset>().Where(a => a.DeviceId.StartsWith("import:")).ToListAsync()).ToList();
    }

    private void Write(string relative, byte[] bytes)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private byte[] Jpeg(string? exifTaken = null, string? make = null, string? model = null, double? lat = null, double? lon = null)
    {
        var v = ++_variant;
        using var image = new MagickImage(MagickColor.FromRgb((byte)(v * 7 % 255), (byte)(v * 13 % 255), 90), 32, 24) { Format = MagickFormat.Jpeg };
        var exif = new ExifProfile();
        if (exifTaken is not null) exif.SetValue(ExifTag.DateTimeOriginal, exifTaken);
        if (make is not null) exif.SetValue(ExifTag.Make, make);
        if (model is not null) exif.SetValue(ExifTag.Model, model);
        if (lat is { } la && lon is { } lo)
        {
            exif.SetValue(ExifTag.GPSLatitude, Dms(la));
            exif.SetValue(ExifTag.GPSLatitudeRef, "N");
            exif.SetValue(ExifTag.GPSLongitude, Dms(lo));
            exif.SetValue(ExifTag.GPSLongitudeRef, "E");
        }

        image.SetProfile(exif);
        return image.ToByteArray();
    }

    private static Rational[] Dms(double value)
    {
        var deg = Math.Floor(value);
        var min = Math.Floor((value - deg) * 60);
        var sec = (value - deg - (min / 60)) * 3600;
        return [new Rational((uint)deg, 1), new Rational((uint)min, 1), new Rational((uint)Math.Round(sec * 1000), 1000)];
    }

    private static byte[] Sidecar(long taken, long upload) =>
        System.Text.Encoding.UTF8.GetBytes($$$"""{"photoTakenTime":{"timestamp":"{{{taken}}}"},"creationTime":{"timestamp":"{{{upload}}}"}}""");
}
