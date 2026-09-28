using LupiraPhotoApi.Core.Domain;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class PhotographerAndDonationTests
{
    private static PhotoAsset Asset() => new() { Id = Guid.NewGuid(), PrincipalId = Guid.NewGuid(), Status = AssetStatus.Ready };

    [Fact]
    public void LowerSource_NeverReplacesAHigherOne()
    {
        var asset = Asset();
        var simon = Guid.NewGuid();
        Photographer.SetManual(asset, simon);

        Assert.False(Photographer.Offer(asset, Guid.NewGuid(), CapturedBySource.Folder));
        Assert.Equal(simon, asset.CapturedByContactId);
    }

    [Fact]
    public void FolderReplacesCameraOwner()
    {
        var asset = Asset();
        Photographer.Offer(asset, Guid.NewGuid(), CapturedBySource.CameraOwner);
        var viktor = Guid.NewGuid();

        Assert.True(Photographer.Offer(asset, viktor, CapturedBySource.Folder));
        Assert.Equal((viktor, CapturedBySource.Folder), (asset.CapturedByContactId!.Value, asset.CapturedBySource!.Value));
    }

    [Fact]
    public void Duplicate_DonatesAlbumPlaceAndPhotographer_WithoutOverwriting()
    {
        var phone = Asset();
        phone.SourceAlbum = null;
        phone.PlaceLabel = "Solna";
        phone.Latitude = 59.38;
        phone.Longitude = 18.02;
        phone.GeotagSource = GeotagSource.ExifGps;
        var copy = Asset();
        copy.SourceAlbum = "2017-10-03 Svalbard";
        copy.SourceAlbumKind = AlbumKind.Event;
        copy.PlaceHint = new PlaceHint { Source = PlaceHintSource.Folder, Latitude = 78.2, Longitude = 15.6, Label = "Longyearbyen" };
        Photographer.Offer(copy, Guid.NewGuid(), CapturedBySource.Folder);

        Assert.True(MetadataDonation.Apply(phone, copy));
        Assert.Equal("2017-10-03 Svalbard", phone.SourceAlbum);
        Assert.Equal(59.38, phone.Latitude);
        Assert.Equal("Longyearbyen", phone.PlaceLabel);
        Assert.Equal(CapturedBySource.Folder, phone.CapturedBySource);
    }

    [Fact]
    public void ExactDate_ReplacesAnApproximateOne()
    {
        var canonical = Asset();
        canonical.TakenAtSource = TakenAtSource.Upload;
        canonical.TakenAt = new DateTimeOffset(2019, 12, 17, 0, 0, 0, TimeSpan.Zero);
        var copy = Asset();
        copy.TakenAtSource = TakenAtSource.Exif;
        copy.TakenAt = new DateTimeOffset(2019, 6, 7, 10, 0, 0, TimeSpan.Zero);

        MetadataDonation.Apply(canonical, copy);
        Assert.Equal((TakenAtSource.Exif, 6), (canonical.TakenAtSource, canonical.TakenAt.Month));
    }
}
