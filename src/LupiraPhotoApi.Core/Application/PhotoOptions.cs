namespace LupiraPhotoApi.Core.Application;

/// <summary>Bound from the <c>Photos</c> section.</summary>
public sealed class PhotoOptions
{
    public const string SectionName = "Photos";

    public long MaxSizeBytes { get; set; } = 8L * 1024 * 1024 * 1024;

    public int PresignPutExpiryMinutes { get; set; } = 60;

    public int OriginalGetExpiryMinutes { get; set; } = 15;

    public int ThumbGetExpiryHours { get; set; } = 24;

    public int MaxProcessingAttempts { get; set; } = 5;

    public int DeclaredExpiryDays { get; set; } = 7;

    public int ProcessingTickSeconds { get; set; } = 10;

    public int ProcessingBatchSize { get; set; } = 4;
}
