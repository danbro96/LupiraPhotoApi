namespace LupiraPhotoApi.Core.Dtos.Photos;

/// <summary>Hand-set metadata. A null contact records the photographer as deliberately unknown.</summary>
public sealed class UpdatePhotoRequest
{
    public required Guid? CapturedByContactId { get; set; }
}
