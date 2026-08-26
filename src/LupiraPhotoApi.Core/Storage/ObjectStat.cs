namespace LupiraPhotoApi.Core.Storage;

public sealed class ObjectStat
{
    public required long SizeBytes { get; set; }
    public string? ContentType { get; set; }
    public string? ETag { get; set; }
}
