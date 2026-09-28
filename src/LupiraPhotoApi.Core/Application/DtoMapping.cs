using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;

namespace LupiraPhotoApi.Core.Application;

internal static class DtoMapping
{
    public static CameraDto? Camera(CameraInfo? camera) => camera is null
        ? null
        : new CameraDto
        {
            Make = camera.Make,
            Model = camera.Model,
            Lens = camera.Lens,
            FocalLengthMm = camera.FocalLengthMm,
            FNumber = camera.FNumber,
            ExposureSeconds = camera.ExposureSeconds,
            Iso = camera.Iso,
            Software = camera.Software,
        };

    /// <summary>"Sony G8341", without doubling a make the model already starts with ("Canon Canon EOS 650D").</summary>
    public static string? CameraName(CameraInfo? camera)
    {
        if (camera?.Model is not { } model) return camera?.Make;
        if (camera.Make is not { } make || model.StartsWith(make, StringComparison.OrdinalIgnoreCase)) return model;
        return $"{make} {model}";
    }
}
