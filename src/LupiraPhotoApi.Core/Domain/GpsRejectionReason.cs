namespace LupiraPhotoApi.Core.Domain;

public enum GpsRejectionReason
{
    /// <summary>Unreachable from both neighbouring fixes of the same camera, while they are close to each other.</summary>
    Spike,

    /// <summary>The same coordinate from one camera on several days — a stale fix, rejected only when named.</summary>
    Repeat,
}
