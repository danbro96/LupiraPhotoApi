namespace LupiraPhotoApi.Core.Dtos.Photos;

public sealed class RetimePhotosResponse
{
    public required int Count { get; set; }

    public required List<RetimedPhotoDto> Items { get; set; }
}
