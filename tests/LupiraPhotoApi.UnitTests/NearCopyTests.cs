using LupiraPhotoApi.Core.Application.Import;
using LupiraPhotoApi.Core.Domain;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class NearCopyTests
{
    private static readonly DateTimeOffset Taken = new(2016, 8, 3, 11, 31, 8, TimeSpan.Zero);
    private static readonly Guid Original = Guid.NewGuid();

    [Theory]
    [InlineData("Photos from 2012/20121010_150957(1).jpg", "Lillstugavägen 11/20121010_150957.jpg")]
    [InlineData("Photos from 2012/20121031_181405.jpg", "Sven & Irene/Sund/2012-10-31 18.14.05.jpg")]
    [InlineData("Photos from 2016/P1010746.JPG", "2016-08-01 Fjällvandring Sarek/Bilder - Simon/P1010746.jpg")]
    [InlineData("a/received_1.jpeg", "b/received_1.jpg")]
    public void Normalize_FoldsCopyRenames(string a, string b) => Assert.Equal(CopyName.Normalize(a), CopyName.Normalize(b));

    [Fact]
    public void Normalize_KeepsAPanoramaApartFromItsFrame() =>
        Assert.NotEqual(CopyName.Normalize("IMG_20150630_130721-PANO.jpg"), CopyName.Normalize("IMG_20150630_130721.jpg"));

    [Fact]
    public void SameNameAndSecond_IsACopy_WhateverTheSize() =>
        Assert.Equal(Original, Index().Find(AssetKind.Photo, "Photos from 2016/P1010746.JPG", Taken, 1_508_722)?.CanonicalId);

    [Fact]
    public void TheNextSecond_IsAnotherShot() =>
        Assert.Null(Index().Find(AssetKind.Photo, "Photos from 2016/P1010746(0).JPG", Taken.AddSeconds(1), 1_508_722));

    [Fact]
    public void AReusedCameraCounter_IsNotACopy() =>
        Assert.Null(Index().Find(AssetKind.Photo, "Photos from 2021/P1010746.JPG", Taken.AddYears(5), 7_038_464));

    [Theory]
    [InlineData(18_283_626, true)]
    [InlineData(10_831_101, false)]
    public void WithinADay_OnlyANearIdenticalSizeIsACopy(long size, bool copy)
    {
        var index = new NearCopyIndex([Stored(AssetKind.Video, "20180414_111319.mp4", 18_283_721)]);
        var found = index.Find(AssetKind.Video, "Lördag i Södertälje/20180414_111319.mp4", Taken.AddHours(2), size);
        Assert.Equal(copy, found is not null);
    }

    [Fact]
    public void ADifferentKind_IsNotACopy() =>
        Assert.Null(Index().Find(AssetKind.Video, "P1010746.JPG", Taken, 7_038_464));

    private static NearCopyIndex Index() => new([Stored(AssetKind.Photo, "2016-08-01 Fjällvandring Sarek/Bilder - Simon/P1010746.JPG", 7_038_464)]);

    private static NearCopy Stored(AssetKind kind, string path, long size) => new()
    {
        CanonicalId = Original,
        Kind = kind,
        Name = CopyName.Normalize(path),
        TakenAt = Taken,
        SizeBytes = size,
    };
}
