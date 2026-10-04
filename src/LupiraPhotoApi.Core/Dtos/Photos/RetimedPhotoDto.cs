namespace LupiraPhotoApi.Core.Dtos.Photos;

public sealed class RetimedPhotoDto
{
    public required Guid Id { get; set; }

    public required DateTimeOffset Before { get; set; }

    public required DateTimeOffset After { get; set; }
}
