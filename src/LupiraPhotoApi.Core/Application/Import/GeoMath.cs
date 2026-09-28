namespace LupiraPhotoApi.Core.Application.Import;

public static class GeoMath
{
    private const double EarthRadiusM = 6_371_000;

    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        static double Rad(double deg) => deg * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
              + (Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        return 2 * EarthRadiusM * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>A folder's own place: the median of its GPS'd photos, trusted only when at least
    /// <paramref name="minPoints"/> of them — and 80% overall — sit within <paramref name="radiusM"/> of it.</summary>
    public static (double Latitude, double Longitude)? AgreedCentroid(
        IReadOnlyCollection<(double Latitude, double Longitude)> points, int minPoints = 3, double radiusM = 1000)
    {
        if (points.Count < minPoints) return null;
        var lat = Median(points.Select(p => p.Latitude));
        var lon = Median(points.Select(p => p.Longitude));
        var near = points.Count(p => DistanceMeters(p.Latitude, p.Longitude, lat, lon) <= radiusM);
        return near >= minPoints && near >= points.Count * 0.8 ? (lat, lon) : null;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
