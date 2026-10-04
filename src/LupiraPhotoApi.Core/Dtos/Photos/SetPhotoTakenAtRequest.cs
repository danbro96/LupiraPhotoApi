namespace LupiraPhotoApi.Core.Dtos.Photos;

/// <summary>A hand-set capture time; outranks every derived time and survives import re-runs until cleared.</summary>
public sealed class SetPhotoTakenAtRequest
{
    public required DateTimeOffset TakenAt { get; set; }
}
