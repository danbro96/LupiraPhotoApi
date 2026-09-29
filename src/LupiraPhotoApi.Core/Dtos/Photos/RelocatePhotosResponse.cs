namespace LupiraPhotoApi.Core.Dtos.Photos;

public sealed class RelocatePhotosResponse
{
    public required int Count { get; set; }

    public required List<Guid> Ids { get; set; }

    /// <summary>The label the moved assets now show; null on a dry run.</summary>
    public string? PlaceLabel { get; set; }
}
