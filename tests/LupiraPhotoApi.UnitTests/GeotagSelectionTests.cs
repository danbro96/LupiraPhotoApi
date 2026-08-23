using LupiraPhotoApi.Application.Processing;
using LupiraPhotoApi.Domain;
using LupiraPhotoApi.Storage;
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
        FakeObjectStore? store = null) =>
        new(store ?? new FakeObjectStore(),
            new FakeThumbnailer(),
            new FakeThumbnailer(),
            new FakeGeocoder(reverseLabel),
            new FakeHistory(historyHit),
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
