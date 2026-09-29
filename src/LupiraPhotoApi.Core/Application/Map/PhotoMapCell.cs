using LupiraPhotoApi.Core.Domain;

namespace LupiraPhotoApi.Core.Application.Map;

/// <summary>Latitude/Longitude are the cell's photos' centroid, and Extent their bounds — not the cell's.</summary>
public sealed record PhotoMapCell(int Count, double Latitude, double Longitude, Bbox Extent, Guid NewestId);
