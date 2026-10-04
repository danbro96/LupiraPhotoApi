namespace LupiraPhotoApi.Core.Application;

public readonly record struct GpsFix(Guid Id, string Camera, DateTimeOffset TakenAt, double Latitude, double Longitude);
