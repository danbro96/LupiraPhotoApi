namespace LupiraPhotoApi.Core.Domain;

/// <summary>Hand-set capture-time corrections. A set applies at once and a clear restores the derived time.</summary>
public static class ManualCaptureTime
{
    public static void Set(PhotoAsset asset, DateTimeOffset takenAt, DateTimeOffset now)
    {
        var utc = takenAt.ToUniversalTime();
        var (derivedAt, derivedSource) = asset.CaptureTimeOverride is { } previous
            ? (previous.DerivedTakenAt, previous.DerivedSource)
            : (asset.TakenAt, asset.TakenAtSource);
        asset.CaptureTimeOverride = new CaptureTimeOverride
        {
            TakenAt = utc,
            SetAt = now,
            DerivedTakenAt = derivedAt,
            DerivedSource = derivedSource,
        };
        (asset.TakenAt, asset.TakenAtSource) = (utc, TakenAtSource.Manual);
    }

    public static bool Clear(PhotoAsset asset)
    {
        if (asset.CaptureTimeOverride is not { } manual) return false;
        (asset.TakenAt, asset.TakenAtSource) = (manual.DerivedTakenAt, manual.DerivedSource);
        asset.CaptureTimeOverride = null;
        return true;
    }

    /// <summary>A derived time lands under an override instead of replacing it.</summary>
    public static void Derive(PhotoAsset asset, DateTimeOffset takenAt, TakenAtSource source)
    {
        if (asset.CaptureTimeOverride is { } manual)
            (manual.DerivedTakenAt, manual.DerivedSource) = (takenAt, source);
        else
            (asset.TakenAt, asset.TakenAtSource) = (takenAt, source);
    }
}
