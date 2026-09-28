using System.Text.RegularExpressions;

namespace LupiraPhotoApi.Core.Application.Import;

/// <summary>A file name as it survives copying between libraries: Google's <c>(1)</c> collision suffix,
/// Nextcloud's <c>2012-10-31 18.14.05</c> camera-upload rename and <c>.jpeg</c> all fold away.</summary>
public static partial class CopyName
{
    public static string Normalize(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        var ext = Path.GetExtension(name);
        var stem = CollisionSuffix().Replace(Path.GetFileNameWithoutExtension(name), string.Empty);
        stem = UploadRename().Replace(stem, "$1$2$3_$4$5$6");
        return stem + (ext == ".jpeg" ? ".jpg" : ext);
    }

    [GeneratedRegex(@"\(\d+\)$")]
    private static partial Regex CollisionSuffix();

    [GeneratedRegex(@"^(\d{4})-(\d\d)-(\d\d) (\d\d)\.(\d\d)\.(\d\d)")]
    private static partial Regex UploadRename();
}
