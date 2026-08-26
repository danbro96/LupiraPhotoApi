namespace LupiraPhotoApi.Core.Dtos.Photos;

public sealed class PhotoListResponse
{
    public required List<PhotoListItemDto> Items { get; set; }
    /// <summary>Opaque keyset cursor; absent on the last page.</summary>
    public string? NextCursor { get; set; }
}
