namespace LupiraPhotoApi.Core.Dtos.Photos;

/// <summary>Hydrates a set of asset ids in one call — used to turn calendar-item relation references
/// into renderable items.</summary>
public sealed class LookupPhotosRequest
{
    public required List<Guid> Ids { get; set; }
}
