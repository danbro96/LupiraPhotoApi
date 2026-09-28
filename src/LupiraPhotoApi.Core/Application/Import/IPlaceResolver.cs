namespace LupiraPhotoApi.Core.Application.Import;

/// <summary>Text → coordinates: the gazetteer first (places the estate already knows, contact addresses
/// among them), then the geocoder. Only a single confident answer counts; ambiguity returns null.</summary>
public interface IPlaceResolver
{
    Task<ResolvedPlace?> ResolveAsync(string query, CancellationToken ct = default);
}
