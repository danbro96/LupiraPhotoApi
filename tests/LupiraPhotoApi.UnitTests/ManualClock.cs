using Microsoft.Extensions.Internal;

namespace LupiraPhotoApi.UnitTests;

public sealed class ManualClock(DateTimeOffset start) : ISystemClock
{
    public DateTimeOffset UtcNow { get; set; } = start;
}
