using LupiraPhotoApi.Core.Application;
using LupiraPhotoApi.Core.Domain;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public sealed class PhotoPresignerTests : IDisposable
{
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly SigningObjectStore _store = new();
    private readonly MemoryCache _cache;
    private readonly PhotoPresigner _presigner;

    public PhotoPresignerTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions { Clock = _clock });
        _presigner = new PhotoPresigner(_store, _cache, Options.Create(new PhotoOptions { ThumbGetExpiryHours = 24, OriginalGetExpiryMinutes = 15 }));
    }

    public void Dispose() => _cache.Dispose();

    private static PhotoAsset Asset(string name) => new()
    {
        Id = Guid.NewGuid(),
        Status = AssetStatus.Ready,
        OriginalKey = $"originals/{name}.jpg",
        ThumbKey = $"thumbs/{name}.webp",
    };

    [Fact]
    public async Task TheSameThumbIsSignedOnceWithinHalfItsExpiry()
    {
        var asset = Asset("a");
        var first = await _presigner.ThumbUrlAsync(asset, default);
        _clock.UtcNow += TimeSpan.FromHours(11.9);

        Assert.Equal(first, await _presigner.ThumbUrlAsync(asset, default));
        Assert.Equal(1, _store.Signed);
    }

    [Fact]
    public async Task AThumbIsResignedOnceHalfItsExpiryHasPassed()
    {
        var asset = Asset("a");
        var first = await _presigner.ThumbUrlAsync(asset, default);
        _clock.UtcNow += TimeSpan.FromHours(12.1);

        Assert.NotEqual(first, await _presigner.ThumbUrlAsync(asset, default));
    }

    [Fact]
    public async Task TheOriginalIsReusedOnItsOwnShorterWindow()
    {
        var asset = Asset("a");
        var first = (await _presigner.ToDtoAsync(asset, includeOriginal: true, default)).OriginalUrl;

        _clock.UtcNow += TimeSpan.FromMinutes(7);
        Assert.Equal(first, (await _presigner.ToDtoAsync(asset, includeOriginal: true, default)).OriginalUrl);

        _clock.UtcNow += TimeSpan.FromMinutes(1);
        Assert.NotEqual(first, (await _presigner.ToDtoAsync(asset, includeOriginal: true, default)).OriginalUrl);
    }

    [Fact]
    public async Task DistinctKeysGetDistinctUrls()
    {
        Assert.NotEqual(await _presigner.ThumbUrlAsync(Asset("a"), default), await _presigner.ThumbUrlAsync(Asset("b"), default));
    }

    [Fact]
    public async Task ForgettingAnAssetDropsBothOfItsUrls()
    {
        var asset = Asset("a");
        var dto = await _presigner.ToDtoAsync(asset, includeOriginal: true, default);

        _presigner.Forget(asset);
        var after = await _presigner.ToDtoAsync(asset, includeOriginal: true, default);

        Assert.NotEqual(dto.ThumbUrl, after.ThumbUrl);
        Assert.NotEqual(dto.OriginalUrl, after.OriginalUrl);
    }
}
