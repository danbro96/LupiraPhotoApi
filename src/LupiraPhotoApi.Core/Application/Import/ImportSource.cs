namespace LupiraPhotoApi.Core.Application.Import;

public enum ImportSource
{
    /// <summary>Event folders (<c>YYYY-MM-DD Title</c>) with person-named subfolders.</summary>
    Handelser,

    /// <summary>Folders named by address or area; places, not albums.</summary>
    Platser,

    /// <summary>The shared family folder: dated events plus undated theme folders.</summary>
    Family,

    /// <summary>A Google Takeout export: year folders, albums, JSON sidecars.</summary>
    Takeout,
}
