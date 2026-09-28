namespace LupiraPhotoApi.Core.Application.Import;

public sealed class TakeoutSidecar
{
    public DateTimeOffset? TakenUtc { get; set; }

    public DateTimeOffset? UploadUtc { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    /// <summary>Came from someone else's shared album — not the user's photo.</summary>
    public bool FromSharedAlbum { get; set; }
}
