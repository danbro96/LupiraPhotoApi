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
        ["video/mp4"] = ("mp4", AssetKind.Video),
        ["video/quicktime"] = ("mov", AssetKind.Video),
        ["video/webm"] = ("webm", AssetKind.Video),
        ["video/x-matroska"] = ("mkv", AssetKind.Video),
        ["video/3gpp"] = ("3gp", AssetKind.Video),
    };

    public static bool TryResolve(string contentType, out string extension, out AssetKind kind)
    {
        if (ContentTypes.TryGetValue(contentType, out var entry))
        {
            (extension, kind) = entry;
            return true;
        }
        extension = "";
        kind = default;
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
