namespace LupiraPhotoApi.Core.Domain;

/// <summary>Who took a photo. Sources rank Manual &gt; Uploader &gt; Folder &gt; CameraOwner; an offer from a
/// lower source never replaces a higher one, so a re-run import can't undo a hand-set photographer.</summary>
public static class Photographer
{
    public static bool Offer(PhotoAsset asset, Guid? contactId, CapturedBySource? source)
    {
        if (source is not { } offered) return false;
        if (asset.CapturedBySource is { } current && Rank(current) > Rank(offered)) return false;
        if (asset.CapturedBySource == offered && asset.CapturedByContactId == contactId) return false;
        asset.CapturedByContactId = contactId;
        asset.CapturedBySource = offered;
        return true;
    }

    /// <summary>A hand-set value; <paramref name="contactId"/> null records "unknown" deliberately.</summary>
    public static void SetManual(PhotoAsset asset, Guid? contactId)
    {
        asset.CapturedByContactId = contactId;
        asset.CapturedBySource = Domain.CapturedBySource.Manual;
    }

    private static int Rank(CapturedBySource source) => source switch
    {
        Domain.CapturedBySource.Manual => 3,
        Domain.CapturedBySource.Uploader => 2,
        Domain.CapturedBySource.Folder => 1,
        _ => 0,
    };
}
