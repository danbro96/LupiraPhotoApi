using LupiraPhotoApi.Core.Application;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class GpsSweepServiceTests
{
    private const double FalsterboLat = 55.390556;
    private const double FalsterboLon = 12.833056;
    private static readonly DateTimeOffset T0 = new(2015, 8, 9, 10, 0, 0, TimeSpan.Zero);

    private static GpsFix Fix(DateTimeOffset at, double lat, double lon, string camera = "SAMSUNG GT-I9300") =>
        new(Guid.NewGuid(), camera, at, lat, lon);

    [Fact]
    public void AFixFarFromBothCloseNeighbours_IsASpike()
    {
        var bad = Fix(T0.AddMinutes(5), FalsterboLat, FalsterboLon);
        var fixes = new[] { Fix(T0, 59.33, 18.07), bad, Fix(T0.AddMinutes(10), 59.331, 18.071) };

        var spike = Assert.Single(GpsSweepService.FindSpikes(fixes));

        Assert.Equal(bad.Id, spike.Id);
        Assert.True(spike.SpeedInKmh > GpsSweepService.SpikeKmh && spike.SpeedOutKmh > GpsSweepService.SpikeKmh);
    }

    [Fact]
    public void AFlight_IsNoSpike()
    {
        var fixes = new[]
        {
            Fix(T0, 59.65, 17.93),
            Fix(T0.AddHours(1), 56.0, 10.0),
            Fix(T0.AddHours(2.5), 51.47, -0.45),
        };

        Assert.Empty(GpsSweepService.FindSpikes(fixes));
    }

    [Fact]
    public void Neighbours_FurtherThanSixHoursAway_AreNotJudged()
    {
        var fixes = new[] { Fix(T0, 59.33, 18.07), Fix(T0.AddHours(7), FalsterboLat, FalsterboLon), Fix(T0.AddHours(7.1), 59.33, 18.07) };

        Assert.Empty(GpsSweepService.FindSpikes(fixes));
    }

    [Fact]
    public void Cameras_AreJudgedApart()
    {
        var fixes = new[]
        {
            Fix(T0, 59.33, 18.07, camera: "a"),
            Fix(T0.AddMinutes(5), FalsterboLat, FalsterboLon, camera: "b"),
            Fix(T0.AddMinutes(10), 59.33, 18.07, camera: "a"),
        };

        Assert.Empty(GpsSweepService.FindSpikes(fixes));
    }

    [Fact]
    public void AStaleFixOverSeveralDays_IsReported_ButRejectedOnlyWhenNamed()
    {
        var fixes = new[]
        {
            Fix(T0, FalsterboLat, FalsterboLon),
            Fix(T0.AddMinutes(1), FalsterboLat, FalsterboLon),
            Fix(T0.AddDays(3), FalsterboLat + 1e-6, FalsterboLon),
            Fix(T0.AddDays(3).AddMinutes(1), 59.33, 18.07),
            Fix(T0, FalsterboLat, FalsterboLon, camera: "Sony G8341"),
        };

        var repeat = Assert.Single(GpsSweepService.FindRepeats(fixes));
        Assert.Equal(("SAMSUNG GT-I9300", 3, 2), (repeat.Camera, repeat.Count, repeat.Days));

        var spikes = GpsSweepService.FindSpikes(fixes);
        Assert.Empty(GpsSweepService.Rejections(fixes, spikes, []));

        var named = GpsSweepService.Rejections(fixes, spikes, [new GpsCoordinateDto { Latitude = FalsterboLat, Longitude = FalsterboLon }]);
        Assert.Equal(4, named.Count);
        Assert.All(named.Values, reason => Assert.Equal(GpsRejectionReason.Repeat, reason));
    }

    [Fact]
    public void Phones_TrackByDevice_ImportsByCamera()
    {
        var camera = new CameraInfo { Make = "SAMSUNG", Model = "GT-I9300" };

        Assert.Equal("device-1", GpsSweepService.CameraKey("device-1", camera));
        Assert.Equal("SAMSUNG GT-I9300", GpsSweepService.CameraKey("import:handelser", camera));
        Assert.Null(GpsSweepService.CameraKey("import:handelser", null));
    }
}
