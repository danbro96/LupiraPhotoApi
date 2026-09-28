namespace LupiraPhotoApi.Core.Domain;

/// <summary>Soft delete. Never touches <see cref="PhotoAsset.Status"/>, so a restore is lossless and the
/// worker's lifecycle stays blind to it.</summary>
public static class AssetTrash
{
    /// <summary>Idempotent: re-trashing keeps the first timestamp, so the retention clock never restarts.</summary>
    public static bool TryTrash(PhotoAsset asset, DateTimeOffset now)
    {
        if (asset.TrashedAt is not null) return false;
        asset.TrashedAt = now;
        return true;
    }

    public static bool TryRestore(PhotoAsset asset)
    {
        if (asset.TrashedAt is null) return false;
        asset.TrashedAt = null;
        return true;
    }

    public static DateTimeOffset? PurgesAt(PhotoAsset asset, TimeSpan retention) => asset.TrashedAt + retention;
}
