namespace LupiraPhotoApi.Core.Domain;

public static class CaptureTime
{
    /// <summary>Sources that only place a photo near its real time; the UI marks them approximate.</summary>
    public static bool IsApproximate(TakenAtSource source) =>
        source is TakenAtSource.Folder or TakenAtSource.Album or TakenAtSource.Upload or TakenAtSource.FileTime;
}
