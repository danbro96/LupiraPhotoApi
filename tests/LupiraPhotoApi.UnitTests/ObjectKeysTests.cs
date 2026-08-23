using LupiraPhotoApi.Domain;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class ObjectKeysTests
{
    private static readonly Guid Principal = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Asset = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Theory]
    [InlineData("image/jpeg", "jpg", AssetKind.Photo)]
    [InlineData("IMAGE/JPEG", "jpg", AssetKind.Photo)]
    [InlineData("image/heic", "heic", AssetKind.Photo)]
    [InlineData("video/mp4", "mp4", AssetKind.Video)]
    [InlineData("video/quicktime", "mov", AssetKind.Video)]
    public void TryResolve_Whitelisted(string contentType, string expectedExt, AssetKind expectedKind)
    {
        Assert.True(ObjectKeys.TryResolve(contentType, out var ext, out var kind));
        Assert.Equal(expectedExt, ext);
        Assert.Equal(expectedKind, kind);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("")]
    public void TryResolve_RejectsEverythingElse(string contentType)
    {
        Assert.False(ObjectKeys.TryResolve(contentType, out _, out _));
    }

    [Fact]
    public void Keys_BucketByPrincipalAndTakenAtMonth()
    {
        var takenAt = new DateTimeOffset(2026, 7, 15, 22, 30, 0, TimeSpan.FromHours(2));

        Assert.Equal(
            "originals/11111111222233334444555555555555/2026/07/aaaaaaaabbbbccccddddeeeeeeeeeeee.jpg",
            ObjectKeys.Original(Principal, takenAt, Asset, "jpg"));
        Assert.Equal(
            "thumbs/11111111222233334444555555555555/2026/07/aaaaaaaabbbbccccddddeeeeeeeeeeee.webp",
            ObjectKeys.Thumb(Principal, takenAt, Asset));
    }

    [Fact]
    public void Keys_UseUtcMonth_NotLocalOffset()
    {
        // 00:30+02:00 on Aug 1 is still July in UTC — the key must not straddle months by timezone.
        var takenAt = new DateTimeOffset(2026, 8, 1, 0, 30, 0, TimeSpan.FromHours(2));
        Assert.Contains("/2026/07/", ObjectKeys.Original(Principal, takenAt, Asset, "jpg"));
    }
}
