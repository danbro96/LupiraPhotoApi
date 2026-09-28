using LupiraPhotoApi.Core.Application;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class PhotoDensityAggregateTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RoundsIntoTheSameCellOrTheNeighbour()
    {
        var cells = PhotoStatsService.Aggregate(
        [
            (59.33012, 18.06988, Noon),
            (59.32961, 18.07049, Noon),
            (59.33061, 18.07, Noon),
        ]);

        Assert.Equal(
            [(59.33, 18.07, 2), (59.331, 18.07, 1)],
            cells.Select(c => (c.Latitude, c.Longitude, c.Count)));
    }

    [Fact]
    public void ManyPhotosOnOneDayCountAsOneDay()
    {
        var cell = Assert.Single(PhotoStatsService.Aggregate(
            Enumerable.Range(0, 5).Select(i => (59.33, 18.07, Noon.AddMinutes(i * 30)))));

        Assert.Equal(5, cell.Count);
        Assert.Equal([new DateOnly(2026, 8, 1)], cell.Days);
    }

    [Fact]
    public void DaysSplitAtUtcMidnight()
    {
        var cell = Assert.Single(PhotoStatsService.Aggregate(
        [
            (59.33, 18.07, new DateTimeOffset(2026, 8, 2, 0, 30, 0, TimeSpan.Zero)),
            (59.33, 18.07, new DateTimeOffset(2026, 8, 1, 23, 30, 0, TimeSpan.Zero)),
        ]));

        Assert.Equal([new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 2)], cell.Days);
    }

    [Fact]
    public void OrdersByDaysThenCount()
    {
        var cells = PhotoStatsService.Aggregate(
        [
            (10.0, 10.0, Noon), (10.0, 10.0, Noon), (10.0, 10.0, Noon),
            (20.0, 20.0, Noon), (20.0, 20.0, Noon.AddDays(1)),
            (30.0, 30.0, Noon), (30.0, 30.0, Noon), (30.0, 30.0, Noon.AddDays(1)),
            (40.0, 40.0, Noon),
        ]);

        Assert.Equal(
            [(30.0, 2, 3), (20.0, 2, 2), (10.0, 1, 3), (40.0, 1, 1)],
            cells.Select(c => (c.Latitude, c.Days.Count, c.Count)));
    }
}
