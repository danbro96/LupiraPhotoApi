using LupiraPhotoApi.Core.Application.Import;
using LupiraPhotoApi.Core.Domain;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class ImportGeometryTests
{
    [Fact]
    public void CoreSpan_IgnoresAStrayDay()
    {
        var days = new[] { "2019-06-06", "2019-06-07", "2019-06-07", "2019-06-09", "2019-12-17" }
            .Select(d => DateOnly.Parse(d, System.Globalization.CultureInfo.InvariantCulture));
        var core = CoreSpan.Compute(days)!.Value;
        Assert.Equal((new DateOnly(2019, 6, 6), new DateOnly(2019, 6, 9), 4), core);
    }

    [Fact]
    public void AgreedCentroid_NeedsThreeNearbyPoints()
    {
        (double, double)[] tight = [(59.3500, 18.0036), (59.3502, 18.0030), (59.3499, 18.0040)];
        Assert.NotNull(GeoMath.AgreedCentroid(tight));
        Assert.Null(GeoMath.AgreedCentroid(tight[..2]));
        Assert.Null(GeoMath.AgreedCentroid([(59.35, 18.00), (59.35, 18.00), (57.70, 11.97), (56.83, 13.94)]));
    }

    [Fact]
    public void Distance_IsHaversine() =>
        Assert.InRange(GeoMath.DistanceMeters(59.3293, 18.0686, 57.7089, 11.9746), 395_000, 400_000);
}
