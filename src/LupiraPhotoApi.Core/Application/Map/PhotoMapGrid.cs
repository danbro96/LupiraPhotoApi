using LupiraPhotoApi.Core.Domain;

namespace LupiraPhotoApi.Core.Application.Map;

/// <summary>Map clusters are Web Mercator tiles three levels below the viewport's MapLibre zoom (a 512 px world
/// at zoom 0): 64–128 px on screen at any latitude, and fixed to the world, so panning never regroups photos.</summary>
public static class PhotoMapGrid
{
    /// <summary>What bounds the response: a viewport spans at most this many cells per side, plus partial edge cells.</summary>
    public const int MaxCellsPerSide = 32;

    public const int MaxLevel = 24;

    private const double MaxMercatorLatitude = 85.05112878;

    /// <summary>Zoom + 3, coarsened until the bbox fits <see cref="MaxCellsPerSide"/>; without a zoom, that fit alone.</summary>
    public static int Level(Bbox bbox, double? zoom)
    {
        var span = Math.Max(X(bbox.MaxLon) - X(bbox.MinLon), Y(bbox.MinLat) - Y(bbox.MaxLat));
        var fit = span > 0 ? (int)Math.Floor(Math.Log2(MaxCellsPerSide / span)) : MaxLevel;
        var level = zoom is { } z ? (int)Math.Floor(Math.Clamp(z, 0, MaxLevel)) + 3 : fit;
        return Math.Clamp(Math.Min(level, fit), 0, MaxLevel);
    }

    public static (int X, int Y) CellOf(double latitude, double longitude, int level)
    {
        var n = 1 << level;
        return (Index(X(longitude), n), Index(Y(latitude), n));
    }

    public static List<PhotoMapCell> Group(IEnumerable<PhotoMapPoint> points, int level) =>
        points.GroupBy(p => CellOf(p.Latitude, p.Longitude, level))
            .Select(g => new PhotoMapCell(
                g.Count(),
                g.Average(p => p.Latitude),
                g.Average(p => p.Longitude),
                new Bbox(g.Min(p => p.Longitude), g.Min(p => p.Latitude), g.Max(p => p.Longitude), g.Max(p => p.Latitude)),
                g.MaxBy(p => (p.TakenAt, p.Id)).Id))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Latitude)
            .ThenBy(c => c.Longitude)
            .ToList();

    private static int Index(double unit, int n) => Math.Clamp((int)Math.Floor(unit * n), 0, n - 1);

    // Normalized to [0, 1], west→east and north→south: the slippy-map tile scheme.
    private static double X(double longitude) => (longitude + 180) / 360;

    private static double Y(double latitude)
    {
        var rad = Math.Clamp(latitude, -MaxMercatorLatitude, MaxMercatorLatitude) * Math.PI / 180;
        return (1 - (Math.Asinh(Math.Tan(rad)) / Math.PI)) / 2;
    }
}
