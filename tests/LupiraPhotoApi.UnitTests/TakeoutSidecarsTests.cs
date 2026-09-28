using LupiraPhotoApi.Core.Application.Import;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class TakeoutSidecarsTests
{
    [Theory]
    [InlineData("DSC_0602.JPG", "DSC_0602.JPG.supplemental-metadata.json")]
    [InlineData("IMG_2895 (1).JPG", "IMG_2895 (1).JPG.supplemental-metadata.json")]
    [InlineData("IMG_1234(1).jpg", "IMG_1234.jpg.supplemental-metadata(1).json")]
    [InlineData("Screenshot_20171006-100320_Messenger.png", "Screenshot_20171006-100320_Messenger.png.supplemental-me.json")]
    public void Finds_EachNamingVariant(string file, string sidecar)
    {
        var siblings = new HashSet<string> { file, sidecar, "unrelated.jpg.supplemental-metadata.json" };
        Assert.Equal(sidecar, TakeoutSidecars.Find(file, siblings));
    }

    [Fact]
    public void Reads_TimesGpsAndSharedOrigin()
    {
        var s = TakeoutSidecars.Read("""
            {"photoTakenTime":{"timestamp":"1559902929"},"creationTime":{"timestamp":"1559989327"},
             "geoData":{"latitude":59.33,"longitude":18.07},"googlePhotosOrigin":{"fromSharedAlbum":{}}}
            """)!;
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1559902929), s.TakenUtc);
        Assert.Equal((59.33, 18.07), (s.Latitude!.Value, s.Longitude!.Value));
        Assert.True(s.FromSharedAlbum);
    }

    [Fact]
    public void ZeroGeoData_MeansNoGps()
    {
        var s = TakeoutSidecars.Read("""{"geoData":{"latitude":0.0,"longitude":0.0}}""")!;
        Assert.Null(s.Latitude);
    }

    [Fact]
    public void Titles_UndoTheFileSystemMangling() =>
        Assert.Equal("_Esquadern_ 2020", TakeoutTitles.Mangle("\"Esquadern\" 2020"));
}
