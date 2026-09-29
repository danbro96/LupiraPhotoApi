namespace LupiraPhotoApi.Core.Application.Map;

public readonly record struct PhotoMapPoint(Guid Id, double Latitude, double Longitude, DateTimeOffset TakenAt);
