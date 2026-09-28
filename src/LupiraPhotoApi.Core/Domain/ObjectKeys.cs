namespace LupiraPhotoApi.Core.Domain;

/// <summary>Object-key scheme and the content-type whitelist. Pure — the key is derived once at declare
/// time and stored on the document; never recomputed against a moved target.</summary>
public static class ObjectKeys
{
    private static readonly Dictionary<string, (string Ext, AssetKind Kind)> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ("jpg", AssetKind.Photo),
        ["image/png"] = ("png", AssetKind.Photo),
        ["image/webp"] = ("webp", AssetKind.Photo),
        ["image/heic"] = ("heic", AssetKind.Photo),
        ["image/heif"] = ("heif", AssetKind.Photo),
        ["image/gif"] = ("gif", AssetKind.Photo),
        ["image/avif"] = ("avif", AssetKind.Photo),
        ["image/x-nikon-nef"] = ("nef", AssetKind.Photo),
        ["video/mp4"] = ("mp4", AssetKind.Video),
        ["video/quicktime"] = ("mov", AssetKind.Video),
        ["video/webm"] = ("webm", AssetKind.Video),
        ["video/x-matroska"] = ("mkv", AssetKind.Video),
        ["video/3gpp"] = ("3gp", AssetKind.Video),
        ["video/x-msvideo"] = ("avi", AssetKind.Video),
        ["video/mpeg"] = ("mpg", AssetKind.Video),
        ["video/mp2t"] = ("m2ts", AssetKind.Video),
    };

    private static readonly Dictionary<string, string> ExtensionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpeg"] = "jpg",
        ["jpe"] = "jpg",
        ["mpeg"] = "mpg",
        ["mts"] = "m2ts",
    };

    public static bool TryResolve(string contentType, out string extension, out AssetKind kind)
    {
        if (ContentTypes.TryGetValue(contentType, out var entry))
        {
            (extension, kind) = entry;
            return true;
        }

        extension = string.Empty;
        kind = default;
        return false;
    }

    /// <summary>File extension (with or without the dot) → the whitelisted content type, for importers
    /// that have a file rather than a MediaStore mime type.</summary>
    public static bool TryResolveExtension(string extension, out string contentType)
    {
        var ext = extension.TrimStart('.');
        if (ExtensionAliases.TryGetValue(ext, out var canonical)) ext = canonical;
        foreach (var (type, entry) in ContentTypes)
        {
            if (string.Equals(entry.Ext, ext, StringComparison.OrdinalIgnoreCase))
            {
                contentType = type;
                return true;
            }
        }

        contentType = string.Empty;
        return false;
    }

    public static string Original(Guid principalId, DateTimeOffset takenAt, Guid assetId, string extension)
    {
        var utc = takenAt.UtcDateTime;
        return $"originals/{principalId:N}/{utc:yyyy}/{utc:MM}/{assetId:N}.{extension}";
    }

    public static string Thumb(Guid principalId, DateTimeOffset takenAt, Guid assetId)
    {
        var utc = takenAt.UtcDateTime;
        return $"thumbs/{principalId:N}/{utc:yyyy}/{utc:MM}/{assetId:N}.webp";
    }
}
