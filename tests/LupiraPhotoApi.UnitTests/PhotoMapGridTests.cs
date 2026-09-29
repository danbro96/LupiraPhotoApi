using LupiraPhotoApi.Core.Application.Map;
using LupiraPhotoApi.Core.Domain;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class PhotoMapGridTests
{
    private static readonly Bbox Sweden = new(10, 55, 25, 69);
    private static readonly Bbox World = new(-180, -85, 180, 85);
    private static readonly DateTimeOffset Noon = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(4.0, 7)]
    [InlineData(4.99, 7)]
    [InlineData(5.0, 8)]
    [InlineData(9.0, 8)]
    [InlineData(null, 8)]
    public void LevelIsZoomPlusThreeCappedByTheViewport(double? zoom, int level)
    {
        Assert.Equal(level, PhotoMapGrid.Level(Sweden, zoom));
    }

    [Fact]
    public void TheWholeWorldFitsThirtyTwoCellsASide()
    {
        Assert.Equal(5, PhotoMapGrid.Level(World, null));
        Assert.Equal(5, PhotoMapGrid.Level(World, 12));
        Assert.Equal(3, PhotoMapGrid.Level(World, 0));
    }

    [Fact]
    public void OutOfRangeZoomsClampToTheGrid()
    {
        var point = new Bbox(18.07, 59.33, 18.07, 59.33);
        Assert.Equal(PhotoMapGrid.MaxLevel, PhotoMapGrid.Level(point, 1000));
        Assert.Equal(PhotoMapGrid.MaxLevel, PhotoMapGrid.Level(point, null));
        Assert.Equal(3, PhotoMapGrid.Level(point, -5));
    }

    [Fact]
    public void CellsAreSlippyMapTiles()
    {
        Assert.Equal((563, 301), PhotoMapGrid.CellOf(59.33, 18.07, 10));
        Assert.Equal((17877, 9695), PhotoMapGrid.CellOf(58.9964, 16.4106, 15));
    }

    [Fact]
    public void EdgesOfTheWorldClampIntoTheOuterCells()
    {
        Assert.Equal((31, 0), PhotoMapGrid.CellOf(90, 180, 5));
        Assert.Equal((0, 31), PhotoMapGrid.CellOf(-90, -180, 5));
    }

    [Fact]
    public void ACellCarriesItsCountCentroidExtentAndNewestPhoto()
    {
        var newest = Guid.NewGuid();
        PhotoMapPoint[] points =
        [
            new(Guid.NewGuid(), 59.0, 16.4, Noon),
            new(newest, 59.02, 16.42, Noon.AddDays(1)),
            new(Guid.NewGuid(), 59.01, 16.44, Noon.AddDays(-1)),
            new(Guid.NewGuid(), 59.33, 18.07, Noon),
        ];

        var cells = PhotoMapGrid.Group(points, level: 7);

        Assert.Equal(2, cells.Count);
        var cell = cells[0];
        Assert.Equal(3, cell.Count);
        Assert.Equal(59.01, cell.Latitude, 9);
        Assert.Equal(16.42, cell.Longitude, 9);
        Assert.Equal(new Bbox(16.4, 59.0, 16.44, 59.02), cell.Extent);
        Assert.Equal(newest, cell.NewestId);
        Assert.Equal(1, cells[1].Count);
    }

    [Fact]
    public void NeighboursAcrossACellEdgeStayApart()
    {
        // Level-7 tiles are 2.8125° wide, so 16.875° (70 × 2.8125 − 180) is an edge.
        PhotoMapPoint[] points = [new(Guid.NewGuid(), 59.0, 16.874, Noon), new(Guid.NewGuid(), 59.0, 16.876, Noon)];

        var cells = PhotoMapGrid.Group(points, level: 7);

        Assert.Equal([1, 1], cells.Select(c => c.Count));
    }

    [Theory]
    [InlineData(-180, -85, 180, 85, null)]
    [InlineData(-180, -85, 180, 85, 20.0)]
    [InlineData(10, 55, 25, 69, 5.5)]
    [InlineData(17.9, 59.2, 18.3, 59.4, 11.2)]
    [InlineData(17.9, 59.2, 18.3, 59.4, 30.0)]
    public void AViewportNeverYieldsMoreThanThirtyThreeCellsASide(double minLon, double minLat, double maxLon, double maxLat, double? zoom)
    {
        var bbox = new Bbox(minLon, minLat, maxLon, maxLat);
        const int steps = 300;
        var points = Enumerable.Range(0, steps * steps).Select(i => new PhotoMapPoint(
            Guid.NewGuid(),
            minLat + ((maxLat - minLat) * (i / steps) / (steps - 1)),
            minLon + ((maxLon - minLon) * (i % steps) / (steps - 1)),
            Noon));

        var cells = PhotoMapGrid.Group(points, PhotoMapGrid.Level(bbox, zoom));

        var perSide = PhotoMapGrid.MaxCellsPerSide + 1;
        Assert.InRange(cells.Count, 1, perSide * perSide);
        Assert.Equal(steps * steps, cells.Sum(c => c.Count));
    }
}
