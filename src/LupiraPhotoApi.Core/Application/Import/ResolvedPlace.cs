namespace LupiraPhotoApi.Core.Application.Import;

public sealed class ResolvedPlace
{
    public required double Latitude { get; set; }

    public required double Longitude { get; set; }

    /// <summary>What answered: the gazetteer's place name or the geocoder's display name.</summary>
    public required string Via { get; set; }
}
