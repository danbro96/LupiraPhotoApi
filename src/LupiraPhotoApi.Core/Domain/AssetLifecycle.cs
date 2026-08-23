namespace LupiraPhotoApi.Domain;

/// <summary>The asset status machine. All transitions mutate the document in place and are the only
/// legal way to move between statuses — endpoints and the worker never set <see cref="PhotoAsset.Status"/>
/// directly, so the reachable state space stays exactly what the unit tests enumerate.</summary>
public static class AssetLifecycle
{
    public static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMinutes(1);

    /// <summary>Declared → Uploaded, once the bytes are verified. Resets retry bookkeeping so a
    /// re-upload after a failure gets a full processing budget.</summary>
    public static bool TryMarkUploaded(PhotoAsset asset, DateTimeOffset now)
    {
        if (asset.Status != AssetStatus.Declared) return false;
        asset.Status = AssetStatus.Uploaded;
        asset.UploadedAt = now;
        asset.Attempts = 0;
        asset.NextAttemptAt = null;
        asset.LastError = null;
        return true;
    }

    /// <summary>True when the worker may claim this asset: due for a first/retried attempt, or holding
    /// an expired lease (crash recovery).</summary>
    public static bool IsClaimable(PhotoAsset asset, DateTimeOffset now) => asset.Status switch
    {
        AssetStatus.Uploaded => asset.NextAttemptAt is null || asset.NextAttemptAt <= now,
        AssetStatus.Processing => asset.LeaseUntil is not null && asset.LeaseUntil < now,
        _ => false,
    };

    public static bool TryClaim(PhotoAsset asset, DateTimeOffset now)
    {
        if (!IsClaimable(asset, now)) return false;
        asset.Status = AssetStatus.Processing;
        asset.LeaseUntil = now + ProcessingLease;
        return true;
    }

    public static bool TryCompleteProcessing(PhotoAsset asset, DateTimeOffset now)
    {
        if (asset.Status != AssetStatus.Processing) return false;
        asset.Status = AssetStatus.Ready;
        asset.ProcessedAt = now;
        asset.LeaseUntil = null;
        asset.NextAttemptAt = null;
        asset.LastError = null;
        return true;
    }

    /// <summary>Processing → Uploaded with exponential backoff while attempts remain; → Failed once
    /// exhausted. Failed is terminal until an explicit reprocess.</summary>
    public static bool TryFailAttempt(PhotoAsset asset, DateTimeOffset now, string error, int maxAttempts)
    {
        if (asset.Status != AssetStatus.Processing) return false;
        asset.Attempts++;
        asset.LastError = error;
        asset.LeaseUntil = null;
        if (asset.Attempts >= maxAttempts)
        {
            asset.Status = AssetStatus.Failed;
            asset.NextAttemptAt = null;
        }
        else
        {
            asset.Status = AssetStatus.Uploaded;
            asset.NextAttemptAt = now + RetryBaseDelay * Math.Pow(2, asset.Attempts - 1);
        }
        return true;
    }

    /// <summary>Ready | Failed → Uploaded with a fresh attempt budget (the reprocess endpoint).</summary>
    public static bool TryReprocess(PhotoAsset asset)
    {
        if (asset.Status is not (AssetStatus.Ready or AssetStatus.Failed)) return false;
        asset.Status = AssetStatus.Uploaded;
        asset.Attempts = 0;
        asset.NextAttemptAt = null;
        asset.LeaseUntil = null;
        asset.LastError = null;
        asset.ProcessedAt = null;
        return true;
    }
}
