namespace LupiraPhotoApi.Core.Domain;

/// <summary>How the photographer was decided; <see cref="Photographer"/> ranks them.</summary>
public enum CapturedBySource
{
    CameraOwner,
    Folder,
    Uploader,
    Manual,
}
