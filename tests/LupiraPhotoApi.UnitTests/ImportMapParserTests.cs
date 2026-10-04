using LupiraPhotoApi.Core.Application.Import;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class ImportMapParserTests
{
    [Fact]
    public void Parses_EveryEntryKind()
    {
        var id = Guid.NewGuid();
        var map = ImportMapParser.Parse($"""
            # cameras
            camera  Sony G8341                 = me
            camera  SAMSUNG     HMX200         = {id}
            person  Bilder - Simon             = Simon Weideskog
            person  2017-03-09 NordicFuzzCon 2017/Namiri = none
            place   Ljungby                    = @Skolgatan 18
            place   Torsby Ringväg 30          = 59.3359,18.4962
            place   Kungshamra 47              = Kungshamra 47, Solna | Kungshamra 47 ~ @Kungshamra 64A 500m
            album   Student                    = none
            album   2025-03-07 Korvfestivalen  = @2025-03-07 Korvfestivalen Munchenbryggeriet
            prefer-filename 2016-03-11 Texas Furry Fiesta 2016
            """);

        Assert.Empty(map.Errors);
        Assert.Equal(PersonRefKind.Me, map.Cameras["sony g8341"].Kind);
        Assert.Equal(id, map.Cameras["SAMSUNG HMX200"].ContactId);
        Assert.Equal(PersonRefKind.Unresolved, map.People["Bilder - Simon"].Kind);
        Assert.Equal(PersonRefKind.None, map.People["2017-03-09 NordicFuzzCon 2017/Namiri"].Kind);
        Assert.Equal("Skolgatan 18", map.Places["Ljungby"].SameAs);
        Assert.Equal(59.3359, map.Places["Torsby Ringväg 30"].Latitude);
        var k = map.Places["Kungshamra 47"];
        Assert.Equal(("Kungshamra 47, Solna", "Kungshamra 47", "Kungshamra 64A", 500d), (k.Query, k.Label, k.CheckNear, k.CheckMeters));
        Assert.Equal(AlbumRuleKind.None, map.Albums["Student"].Kind);
        Assert.Equal("2025-03-07 Korvfestivalen Munchenbryggeriet", map.Albums["2025-03-07 Korvfestivalen"].SameAs);
        Assert.Contains("2016-03-11 Texas Furry Fiesta 2016", map.PreferFilename);
    }

    [Fact]
    public void Parses_ClockLines()
    {
        var map = ImportMapParser.Parse("""
            clock  Nexus 5          = utc
            clock  HTC   Desire     = +01:00
            clock  Canon EOS 5D     = -05:30
            clock  SM-G930F         = Europe/Stockholm
            """);

        Assert.Empty(map.Errors);
        Assert.Same(TimeZoneInfo.Utc, map.Clocks["nexus 5"]);
        Assert.Equal(TimeSpan.FromHours(1), map.Clocks["HTC Desire"].GetUtcOffset(new DateTime(2011, 7, 1)));
        Assert.Equal(new TimeSpan(-5, -30, 0), map.Clocks["Canon EOS 5D"].BaseUtcOffset);
        Assert.Equal(TimeSpan.FromHours(2), map.Clocks["SM-G930F"].GetUtcOffset(new DateTime(2011, 7, 1)));
    }

    [Theory]
    [InlineData("clock Nexus 5 = Mars/Olympus")]
    [InlineData("clock Nexus 5 = +1")]
    [InlineData("camera Sony")]
    [InlineData("gadget X = y")]
    [InlineData("album X = maybe")]
    public void BadLines_AreReportedWithTheirLine(string line)
    {
        var map = ImportMapParser.Parse($"# header\n{line}");
        Assert.Contains(map.Errors, e => e.StartsWith("line 2:", StringComparison.Ordinal));
    }
}
