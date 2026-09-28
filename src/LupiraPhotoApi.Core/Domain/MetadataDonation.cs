namespace LupiraPhotoApi.Core.Domain;

/// <summary>A duplicate hands its metadata to the canonical it collapsed onto. A phone backup is usually
/// canonical before its curated copy is imported, so without this the event folder, place and photographer
/// would vanish with the duplicate. Fill-only, except that a curated place label beats a derived one.</summary>
public static class MetadataDonation
{
    public static bool Apply(PhotoAsset canonical, PhotoAsset duplicate)
    {
        var changed = false;

        if (canonical.SourceAlbum is null && duplicate.SourceAlbum is not null)
        {
            canonical.SourceAlbum = duplicate.SourceAlbum;
            canonical.SourceAlbumKind = duplicate.SourceAlbumKind;
            canonical.SourceAlbumDate = duplicate.SourceAlbumDate;
            changed = true;
        }

        if (duplicate.PlaceHint is { } hint && canonical.PlaceHint is null)
        {
            canonical.PlaceHint = hint;
            if (canonical.Latitude is null && hint is { Latitude: not null, Longitude: not null })
            {
                canonical.Latitude = hint.Latitude;
                canonical.Longitude = hint.Longitude;
                canonical.GeotagSource = hint.Source == PlaceHintSource.Folder ? GeotagSource.Folder : GeotagSource.ExifGps;
            }

            if (hint.Label is not null) canonical.PlaceLabel = hint.Label;
            changed = true;
        }

        changed |= Photographer.Offer(canonical, duplicate.CapturedByContactId, duplicate.CapturedBySource);

        if (CaptureTime.IsApproximate(canonical.TakenAtSource) && !CaptureTime.IsApproximate(duplicate.TakenAtSource))
        {
            canonical.TakenAt = duplicate.TakenAt;
            canonical.TakenAtSource = duplicate.TakenAtSource;
            changed = true;
        }

        return changed;
    }
}
