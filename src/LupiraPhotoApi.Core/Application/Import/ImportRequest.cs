namespace LupiraPhotoApi.Core.Application.Import;

public sealed class ImportRequest
{
    public required string Root { get; set; }

    public required ImportSource Source { get; set; }

    public required Guid PrincipalId { get; set; }

    /// <summary>Resolves <c>me</c> in the map file.</summary>
    public Guid? MeContactId { get; set; }

    public required ImportMap Map { get; set; }

    public required bool DryRun { get; set; }

    /// <summary>The zone EXIF wall-clock times are read in when a file carries no offset.</summary>
    public required TimeZoneInfo Zone { get; set; }
}
