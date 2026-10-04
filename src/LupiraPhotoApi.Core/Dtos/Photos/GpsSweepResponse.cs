namespace LupiraPhotoApi.Core.Dtos.Photos;

/// <summary>Counts over everything scanned; <see cref="Spikes"/> and <see cref="Repeats"/> are capped samples.</summary>
public sealed class GpsSweepResponse
{
    public required int Scanned { get; set; }

    public required int SpikeCount { get; set; }

    public required List<GpsSpikeDto> Spikes { get; set; }

    public required int RepeatCount { get; set; }

    /// <summary>Most days first.</summary>
    public required List<GpsRepeatDto> Repeats { get; set; }

    /// <summary>Assets rejected and re-queued; 0 unless applied.</summary>
    public required int Rejected { get; set; }

    /// <summary>The rejected assets, for a restore; at most 2000.</summary>
    public required List<Guid> RejectedIds { get; set; }
}
