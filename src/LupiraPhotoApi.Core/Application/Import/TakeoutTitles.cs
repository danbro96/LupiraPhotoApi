using System.Text.Json;

namespace LupiraPhotoApi.Core.Application.Import;

/// <summary>Takeout replaces characters a file system can't hold with <c>_</c> in album folder names
/// (<c>_Esquadern_ 2020</c>); <c>user-generated-memory-titles.json</c> keeps the real titles
/// (<c>"Esquadern" 2020</c>).</summary>
public static class TakeoutTitles
{
    private const string FileName = "user-generated-memory-titles.json";
    private static readonly char[] Unsafe = ['"', '*', '/', ':', '<', '>', '?', '\\', '|'];

    public static Dictionary<string, string> Load(string root)
    {
        var path = Path.Combine(root, FileName);
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return titles;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("title", out var list)) return titles;
            foreach (var title in list.EnumerateArray().Select(t => t.GetString()).OfType<string>())
                titles[Mangle(title)] = title;
        }
        catch (JsonException)
        {
        }

        return titles;
    }

    public static string Mangle(string title)
    {
        var chars = title.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (Array.IndexOf(Unsafe, chars[i]) >= 0) chars[i] = '_';
        return new string(chars);
    }
}
