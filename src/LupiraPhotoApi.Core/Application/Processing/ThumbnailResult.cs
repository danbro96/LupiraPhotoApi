namespace LupiraPhotoApi.Core.Application.Processing;

public sealed class ThumbnailResult
{
    public required byte[] WebpBytes { get; set; }
    /// <summary>Dimensions of the SOURCE (not the thumb) — backfills assets the client declared without them.</summary>
    public int? SourceWidth { get; set; }
    public int? SourceHeight { get; set; }
}
