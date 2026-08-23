namespace LupiraPhotoApi.Storage;

public sealed class ObjectStat
{
    public required long SizeBytes { get; set; }
    public string? ContentType { get; set; }
    public string? ETag { get; set; }
}

/// <summary>Vendor-neutral object-store seam. Reads and uploads are presigned — bytes never proxy
/// through the API except the worker's own thumbnail round-trip.</summary>
public interface IObjectStore
{
    Task<Uri> PresignPutAsync(string key, string contentType, TimeSpan expiry, CancellationToken ct = default);
    Task<Uri> PresignGetAsync(string key, TimeSpan expiry, CancellationToken ct = default);
    /// <summary>Null when the object does not exist.</summary>
    Task<ObjectStat?> HeadAsync(string key, CancellationToken ct = default);
    Task<Stream> GetStreamAsync(string key, CancellationToken ct = default);
    Task PutAsync(string key, Stream content, long length, string contentType, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    /// <summary>Existence check only — buckets are provisioned via the store's CLI, keys can't create them.</summary>
    Task EnsureBucketAsync(CancellationToken ct = default);
}
