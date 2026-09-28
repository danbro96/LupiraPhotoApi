using LupiraPhotoApi.Core.Domain;

namespace LupiraPhotoApi.Core.Application.Import;

public sealed class CaptureDecision
{
    public required DateTimeOffset TakenAt { get; set; }

    public required TakenAtSource Source { get; set; }

    /// <summary>Internal dates dropped as implausible, for the dry run.</summary>
    public List<string> Rejected { get; } = [];

    /// <summary>Set when EXIF and a capture-style file name disagree by more than a day.</summary>
    public (DateTimeOffset Exif, DateTimeOffset Filename)? Disagreement { get; set; }
}
