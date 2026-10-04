using LupiraPhotoApi.Core.Application.Processing;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class GeotagSelectionTests
{
    private static readonly DateTimeOffset TakenAt = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    private static PhotoAsset Asset(double? lat = null, double? lon = null) => new()
    {
        Id = Guid.NewGuid(),
        PrincipalId = Guid.NewGuid(),
        Kind = AssetKind.Photo,
        Status = AssetStatus.Processing,
        TakenAt = TakenAt,
        Latitude = lat,
        Longitude = lon,
        OriginalKey = "originals/x/y.jpg",
    };

    private static PhotoProcessingService Service(
        string? reverseLabel = null,
        LocationHistoryHit? historyHit = null,
        FakeObjectStore? store = null,
        MediaMetadata? file = null) =>
        new(
            store ?? new FakeObjectStore(),
            new FakeThumbnailer(),
            new FakeThumbnailer(),
            new FakeGeocoder(reverseLabel),
            new FakeHistory(historyHit),
            new FakeMetadata(file ?? MediaMetadata.Empty),
            NullLogger<PhotoProcessingService>.Instance);

    [Fact]
    public async Task ClientCoordinates_Win_AndGetReverseLabel()
    {
        var asset = Asset(59.33, 18.07);
        await Service(reverseLabel: "Stockholm", historyHit: new LocationHistoryHit { Latitude = 0, Longitude = 0, Label = "Wrong" })
            .ProcessAsync(asset, "sub-1", CancellationToken.None);

        Assert.Equal(GeotagSource.ExifGps, asset.GeotagSource);
        Assert.Equal("Stockholm", asset.PlaceLabel);
        Assert.Equal(59.33, asset.Latitude);
    }

    [Fact]
    public async Task ReverseLabelFailure_IsSoft_SourceStaysExifGps()
    {
        var asset = Asset(59.33, 18.07);
        await Service(reverseLabel: null).ProcessAsync(asset, "sub-1", CancellationToken.None);

        Assert.Equal(GeotagSource.ExifGps, asset.GeotagSource);
        Assert.Null(asset.PlaceLabel);
    }

    [Fact]
    public async Task NoCoordinates_FallsBackToLocationHistory()
    {
        var asset = Asset();
        var hit = new LocationHistoryHit { Latitude = 56.05, Longitude = 14.15, Label = "Home" };
        await Service(historyHit: hit).ProcessAsync(asset, "sub-1", CancellationToken.None);

        Assert.Equal(GeotagSource.LocationHistory, asset.GeotagSource);
        Assert.Equal(56.05, asset.Latitude);
        Assert.Equal(14.15, asset.Longitude);
        Assert.Equal("Home", asset.PlaceLabel);
    }

    [Fact]
    public async Task NoCoordinates_NoHistoryHit_EndsAsNone()
    {
        var asset = Asset();
        await Service().ProcessAsync(asset, "sub-1", CancellationToken.None);

        Assert.Equal(GeotagSource.None, asset.GeotagSource);
        Assert.Null(asset.Latitude);
        Assert.Null(asset.PlaceLabel);
    }

    [Fact]
    public async Task Processing_StoresThumb_AndBackfillsDimensions()
    {
        var store = new FakeObjectStore();
        var asset = Asset();
        await Service(store: store).ProcessAsync(asset, "sub-1", CancellationToken.None);

        Assert.NotNull(asset.ThumbKey);
        Assert.True(store.Objects.ContainsKey(asset.ThumbKey!));
        Assert.Equal(640, asset.Width);
        Assert.Equal(480, asset.Height);
    }

    [Fact]
    public async Task FileGps_BeatsAFolderHint_ButKeepsItsCuratedLabel()
    {
        var asset = Asset();
        asset.PlaceHint = new PlaceHint { Source = PlaceHintSource.Folder, Latitude = 59.0, Longitude = 18.0, Label = "Armégatan 32B" };
        await Service(reverseLabel: "Solna", file: new MediaMetadata { Latitude = 59.35, Longitude = 18.00 })
            .ProcessAsync(asset, "sub-1", CancellationToken.None);

        Assert.Equal(GeotagSource.ExifGps, asset.GeotagSource);
        Assert.Equal(59.35, asset.Latitude);
        Assert.Equal("Armégatan 32B", asset.PlaceLabel);
    }

    [Fact]
    public async Task FolderHint_FillsAPhotoWithoutGps_AndBeatsLocationHistory()
    {
        var asset = Asset();
        asset.PlaceHint = new PlaceHint { Source = PlaceHintSource.Folder, Latitude = 56.83, Longitude = 13.94, Label = "Skolgatan 18" };
        await Service(historyHit: new LocationHistoryHit { Latitude = 1, Longitude = 1, Label = "Wrong" })
            .ProcessAsync(asset, "sub-1", CancellationToken.None);

        Assert.Equal(GeotagSource.Folder, asset.GeotagSource);
        Assert.Equal(56.83, asset.Latitude);
        Assert.Equal("Skolgatan 18", asset.PlaceLabel);
    }

    [Fact]
    public async Task LabelOnlyHint_KeepsHistoryCoordinates()
    {
        var asset = Asset();
        asset.PlaceHint = new PlaceHint { Source = PlaceHintSource.Folder, Label = "Hammarby Sjöstad" };
        await Service(historyHit: new LocationHistoryHit { Latitude = 59.30, Longitude = 18.10, Label = "Home" })
            .ProcessAsync(asset, "sub-1", CancellationToken.None);

        Assert.Equal(GeotagSource.LocationHistory, asset.GeotagSource);
        Assert.Equal("Hammarby Sjöstad", asset.PlaceLabel);
    }

    [Fact]
    public async Task ManualLocation_BeatsAStaleFileFix_AndKeepsItsLabel()
    {
        var asset = Asset();
        ManualLocation.Set(asset, 59.02, 16.47, "Mormor", "Mormor", TakenAt);
        await Service(reverseLabel: "Skanör med Falsterbo", file: new MediaMetadata { Latitude = 55.390556, Longitude = 12.833056 })
            .ProcessAsync(asset, "sub-1", CancellationToken.None);

        Assert.Equal(GeotagSource.Manual, asset.GeotagSource);
        Assert.Equal(59.02, asset.Latitude);
        Assert.Equal(16.47, asset.Longitude);
        Assert.Equal("Mormor", asset.PlaceLabel);
    }

    [Fact]
    public async Task ManualLocation_WithoutALabel_IsReverseGeocoded()
    {
        var asset = Asset();
        ManualLocation.Set(asset, 59.02, 16.47, null, null, TakenAt);
        await Service(reverseLabel: "Sköldinge").ProcessAsync(asset, "sub-1", CancellationToken.None);

        Assert.Equal(GeotagSource.Manual, asset.GeotagSource);
        Assert.Equal("Sköldinge", asset.PlaceLabel);
    }

    [Fact]
    public async Task ClearedOverride_OnALegacyPhoneAsset_FallsBackToItsOwnFix()
    {
        var asset = Asset(59.33, 18.07);
        asset.GeotagSource = GeotagSource.ExifGps;
        ManualLocation.Set(asset, 59.02, 16.47, null, null, TakenAt);
        ManualLocation.Clear(asset);
        await Service(reverseLabel: "Stockholm").ProcessAsync(asset, "sub-1", CancellationToken.None);

        Assert.Equal(GeotagSource.ExifGps, asset.GeotagSource);
        Assert.Equal(59.33, asset.Latitude);
        Assert.Equal(18.07, asset.Longitude);
    }

    [Fact]
    public async Task RejectedFileFix_FallsThroughToLocationHistory()
    {
        var asset = Asset();
        asset.GpsRejection = new GpsRejection { Latitude = 55.390556, Longitude = 12.833056, Reason = GpsRejectionReason.Repeat, At = TakenAt };
        await Service(reverseLabel: "Skanör med Falsterbo", historyHit: new LocationHistoryHit { Latitude = 59.30, Longitude = 18.10, Label = "Home" }, file: new MediaMetadata { Latitude = 55.390556, Longitude = 12.833056 })
            .ProcessAsync(asset, "sub-1", CancellationToken.None);

        Assert.Equal(GeotagSource.LocationHistory, asset.GeotagSource);
        Assert.Equal((59.30, "Home"), (asset.Latitude!.Value, asset.PlaceLabel));
    }

    [Fact]
    public async Task RejectedPhoneFix_WithoutHistory_EndsWithoutALocation()
    {
        var asset = Asset(59.9, 30.3);
        asset.PlaceHint = new PlaceHint { Source = PlaceHintSource.Device, Latitude = 59.9, Longitude = 30.3 };
        asset.GpsRejection = new GpsRejection { Latitude = 59.9, Longitude = 30.3, Reason = GpsRejectionReason.Spike, At = TakenAt };
        await Service(reverseLabel: "Sankt Petersburg").ProcessAsync(asset, "sub-1", CancellationToken.None);

        Assert.Equal(GeotagSource.None, asset.GeotagSource);
        Assert.Null(asset.Latitude);
        Assert.Null(asset.PlaceLabel);
    }

    [Fact]
    public async Task ARejection_LeavesOtherFixesAlone()
    {
        var asset = Asset();
        asset.GpsRejection = new GpsRejection { Latitude = 55.390556, Longitude = 12.833056, Reason = GpsRejectionReason.Repeat, At = TakenAt };
        await Service(reverseLabel: "Stockholm", file: new MediaMetadata { Latitude = 59.33, Longitude = 18.07 })
            .ProcessAsync(asset, "sub-1", CancellationToken.None);

        Assert.Equal((GeotagSource.ExifGps, 59.33), (asset.GeotagSource, asset.Latitude!.Value));
    }

    [Fact]
    public async Task Camera_ComesFromTheFile()
    {
        var asset = Asset();
        await Service(file: new MediaMetadata { Camera = new CameraInfo { Make = "Sony", Model = "G8341" } })
            .ProcessAsync(asset, "sub-1", CancellationToken.None);

        Assert.Equal("G8341", asset.Camera?.Model);
    }

    private sealed class FakeMetadata(MediaMetadata metadata) : IMediaMetadataReader
    {
        public Task<MediaMetadata> ReadAsync(string path, AssetKind kind, CancellationToken ct = default) =>
            Task.FromResult(metadata);
    }

    private sealed class FakeThumbnailer : IPhotoThumbnailer, IVideoThumbnailer
    {
        public Task<ThumbnailResult> CreateAsync(string sourcePath, CancellationToken ct = default) =>
            Task.FromResult(new ThumbnailResult { WebpBytes = [1, 2, 3], SourceWidth = 640, SourceHeight = 480 });
    }

    private sealed class FakeGeocoder(string? label) : IReverseGeocoder
    {
        public Task<string?> ReverseLabelAsync(double latitude, double longitude, CancellationToken ct = default) =>
            Task.FromResult(label);
    }

    private sealed class FakeHistory(LocationHistoryHit? hit) : ILocationHistoryClient
    {
        public Task<LocationHistoryHit?> PlaceAtAsync(string authentikSub, DateTimeOffset ts, CancellationToken ct = default) =>
            Task.FromResult(hit);
    }

    private sealed class FakeObjectStore : IObjectStore
    {
        public Dictionary<string, byte[]> Objects { get; } = new() { ["originals/x/y.jpg"] = [9, 9, 9] };

        public Task<Uri> PresignPutAsync(string key, string contentType, TimeSpan expiry, CancellationToken ct = default) =>
            Task.FromResult(new Uri($"http://fake/{key}"));

        public Task<Uri> PresignGetAsync(string key, TimeSpan expiry, CancellationToken ct = default) =>
            Task.FromResult(new Uri($"http://fake/{key}"));

        public Task<ObjectStat?> HeadAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(Objects.TryGetValue(key, out var bytes) ? new ObjectStat { SizeBytes = bytes.Length } : null);

        public Task<Stream> GetStreamAsync(string key, CancellationToken ct = default) =>
            Task.FromResult<Stream>(new MemoryStream(Objects[key]));

        public async Task PutAsync(string key, Stream content, long length, string contentType, CancellationToken ct = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            Objects[key] = buffer.ToArray();
        }

        public Task DeleteAsync(string key, CancellationToken ct = default)
        {
            Objects.Remove(key);
            return Task.CompletedTask;
        }

        public Task EnsureBucketAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
