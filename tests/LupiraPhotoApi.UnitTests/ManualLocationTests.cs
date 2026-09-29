using LupiraPhotoApi.Core.Domain;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class ManualLocationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private static PhotoAsset Asset() => new()
    {
        Id = Guid.NewGuid(),
        PrincipalId = Guid.NewGuid(),
        Kind = AssetKind.Photo,
        Status = AssetStatus.Ready,
        TakenAt = Now,
        OriginalKey = "originals/x/y.jpg",
    };

    [Fact]
    public void Set_AppliesAtOnce()
    {
        var asset = Asset();
        ManualLocation.Set(asset, 59.02, 16.47, null, "Sköldinge", Now);

        Assert.Equal(GeotagSource.Manual, asset.GeotagSource);
        Assert.Equal((59.02, 16.47), (asset.Latitude!.Value, asset.Longitude!.Value));
        Assert.Equal("Sköldinge", asset.PlaceLabel);
        Assert.Null(asset.LocationOverride!.Label);
        Assert.Equal(Now, asset.LocationOverride.SetAt);
    }

    [Fact]
    public void Set_PinsALegacyPhoneFix_AsTheHint()
    {
        var asset = Asset();
        (asset.Latitude, asset.Longitude, asset.GeotagSource) = (59.33, 18.07, GeotagSource.ExifGps);

        ManualLocation.Set(asset, 59.02, 16.47, "Mormor", "Mormor", Now);

        Assert.Equal(PlaceHintSource.Device, asset.PlaceHint!.Source);
        Assert.Equal((59.33, 18.07), (asset.PlaceHint.Latitude!.Value, asset.PlaceHint.Longitude!.Value));
    }

    [Fact]
    public void Set_KeepsAnExistingHint()
    {
        var asset = Asset();
        var hint = new PlaceHint { Source = PlaceHintSource.Folder, Latitude = 56.83, Longitude = 13.94, Label = "Skolgatan 18" };
        asset.PlaceHint = hint;

        ManualLocation.Set(asset, 59.02, 16.47, null, null, Now);

        Assert.Same(hint, asset.PlaceHint);
    }

    [Fact]
    public void Clear_ReportsWhetherThereWasAnOverride()
    {
        var asset = Asset();
        Assert.False(ManualLocation.Clear(asset));

        ManualLocation.Set(asset, 59.02, 16.47, null, null, Now);
        Assert.True(ManualLocation.Clear(asset));
        Assert.Null(asset.LocationOverride);
    }
}
