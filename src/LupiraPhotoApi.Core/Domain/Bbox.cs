namespace LupiraPhotoApi.Domain;

/// <summary>Viewport bounding box, wire format <c>minLon,minLat,maxLon,maxLat</c> (the MapLibre bounds order).</summary>
public readonly record struct Bbox(double MinLon, double MinLat, double MaxLon, double MaxLat)
{
    public static bool TryParse(string? value, out Bbox bbox)
    {
        bbox = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split(',');
        if (parts.Length != 4) return false;
        var n = new double[4];
        for (var i = 0; i < 4; i++)
            if (!double.TryParse(parts[i], System.Globalization.CultureInfo.InvariantCulture, out n[i])) return false;
        if (n[0] > n[2] || n[1] > n[3]) return false;
        if (n[1] < -90 || n[3] > 90 || n[0] < -180 || n[2] > 180) return false;
        bbox = new Bbox(n[0], n[1], n[2], n[3]);
        return true;
    }
}
