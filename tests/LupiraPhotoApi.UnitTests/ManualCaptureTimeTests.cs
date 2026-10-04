using LupiraPhotoApi.Core.Domain;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class ManualCaptureTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Derived = new(2014, 7, 12, 13, 0, 0, TimeSpan.Zero);

    private static PhotoAsset Asset() => new()
    {
        Id = Guid.NewGuid(),
        PrincipalId = Guid.NewGuid(),
        Kind = AssetKind.Photo,
        Status = AssetStatus.Ready,
        TakenAt = Derived,
        TakenAtSource = TakenAtSource.Exif,
        OriginalKey = "originals/x/y.jpg",
    };

    [Fact]
    public void SetThenClear_RestoresTheDerivedTime()
    {
        var asset = Asset();
        var manual = new DateTimeOffset(2014, 7, 12, 14, 0, 0, TimeSpan.FromHours(2));

        ManualCaptureTime.Set(asset, manual, Now);

        Assert.Equal((TakenAtSource.Manual, manual), (asset.TakenAtSource, asset.TakenAt));
        Assert.Equal(TimeSpan.Zero, asset.TakenAt.Offset);
        Assert.Equal((Derived, TakenAtSource.Exif, Now), (asset.CaptureTimeOverride!.DerivedTakenAt, asset.CaptureTimeOverride.DerivedSource, asset.CaptureTimeOverride.SetAt));

        Assert.True(ManualCaptureTime.Clear(asset));
        Assert.Equal((TakenAtSource.Exif, Derived), (asset.TakenAtSource, asset.TakenAt));
        Assert.Null(asset.CaptureTimeOverride);
        Assert.False(ManualCaptureTime.Clear(asset));
    }

    [Fact]
    public void SettingTwice_KeepsTheOriginalDerivedTime()
    {
        var asset = Asset();
        ManualCaptureTime.Set(asset, Derived.AddHours(-1), Now);
        ManualCaptureTime.Set(asset, Derived.AddHours(-2), Now);

        Assert.Equal((Derived, TakenAtSource.Exif), (asset.CaptureTimeOverride!.DerivedTakenAt, asset.CaptureTimeOverride.DerivedSource));
        Assert.Equal(Derived.AddHours(-2), asset.TakenAt);
    }

    [Fact]
    public void Derive_UnderAnOverride_RefreshesOnlyTheDerivedPair()
    {
        var asset = Asset();
        ManualCaptureTime.Set(asset, Derived.AddHours(-1), Now);

        ManualCaptureTime.Derive(asset, Derived.AddDays(1), TakenAtSource.Filename);

        Assert.Equal((TakenAtSource.Manual, Derived.AddHours(-1)), (asset.TakenAtSource, asset.TakenAt));
        Assert.Equal((Derived.AddDays(1), TakenAtSource.Filename), (asset.CaptureTimeOverride!.DerivedTakenAt, asset.CaptureTimeOverride.DerivedSource));
    }

    [Fact]
    public void Derive_WithoutAnOverride_SetsTheTime()
    {
        var asset = Asset();

        ManualCaptureTime.Derive(asset, Derived.AddDays(1), TakenAtSource.Filename);

        Assert.Equal((TakenAtSource.Filename, Derived.AddDays(1)), (asset.TakenAtSource, asset.TakenAt));
    }
}
