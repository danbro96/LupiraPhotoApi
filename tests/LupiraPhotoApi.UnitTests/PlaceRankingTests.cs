using LupiraPhotoApi.Core.Application;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class PlaceRankingTests
{
    [Fact]
    public void OrdersByCountThenLabel()
    {
        var ranked = PhotoStatsService.RankPlaces(["Uppsala", "Stockholm", "Lund", "Stockholm", "Uppsala", "Stockholm", "Kiruna"], limit: null);

        Assert.Equal(
            [("Stockholm", 3), ("Uppsala", 2), ("Kiruna", 1), ("Lund", 1)],
            ranked.Select(p => (p.Label, p.Count)));
    }

    [Theory]
    [InlineData(null, PhotoStatsService.DefaultPlaceLimit)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    [InlineData(500, PhotoStatsService.MaxPlaceLimit)]
    public void ClampsTheLimit(int? limit, int expected)
    {
        var labels = Enumerable.Range(0, 100).Select(i => $"Place {i:D3}");
        Assert.Equal(expected, PhotoStatsService.RankPlaces(labels, limit).Count);
    }
}
