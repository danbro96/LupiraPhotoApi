namespace LupiraPhotoApi.Core.Domain;

/// <summary>A hand-set capture time. Keeps the derived pair it replaced, so an import re-run refreshes that pair
/// instead of the shown time, duplicate lookups still find the asset by it, and a clear restores it.</summary>
public sealed class CaptureTimeOverride
{
    public required DateTimeOffset TakenAt { get; set; }

    public required DateTimeOffset SetAt { get; set; }

    public required DateTimeOffset DerivedTakenAt { get; set; }

    public required TakenAtSource DerivedSource { get; set; }
}
