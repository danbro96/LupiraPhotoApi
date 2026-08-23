namespace LupiraPhotoApi.Domain;

public enum AssetKind
{
    Photo,
    Video,
}

public enum AssetStatus
{
    /// <summary>Metadata registered, presigned PUT issued, bytes not yet confirmed.</summary>
    Declared,

    /// <summary>Bytes verified in the object store; waiting for the processing worker.</summary>
    Uploaded,

    /// <summary>Claimed by the worker (lease-bound; an expired lease is reclaimable after a crash).</summary>
    Processing,

    /// <summary>Thumbnail + geotag done; queryable on the map.</summary>
    Ready,

    /// <summary>Processing exhausted its attempts; terminal until an explicit reprocess.</summary>
    Failed,
}

public enum GeotagSource
{
    None,
    ExifGps,
    LocationHistory,
}
