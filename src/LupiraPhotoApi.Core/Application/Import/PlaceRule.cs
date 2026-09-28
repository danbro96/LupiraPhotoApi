namespace LupiraPhotoApi.Core.Application.Import;

/// <summary>One <c>place</c> line: exactly one of <see cref="SameAs"/>, <see cref="Latitude"/>/<see cref="Longitude"/>
/// or <see cref="Query"/>, plus an optional label override and distance check.</summary>
public sealed class PlaceRule
{
    public string? SameAs { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? Query { get; set; }

    public string? Label { get; set; }

    public string? CheckNear { get; set; }

    public double? CheckMeters { get; set; }
}
