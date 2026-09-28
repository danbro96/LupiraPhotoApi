namespace LupiraPhotoApi.Core.Domain;

/// <summary>Where <see cref="PhotoAsset.TakenAt"/> came from. <see cref="Device"/> is first so documents
/// written before the field existed (phone backups) read as it.</summary>
public enum TakenAtSource
{
    Device,
    Exif,
    Sidecar,
    Filename,
    Folder,
    Album,
    Upload,
    FileTime,
}
