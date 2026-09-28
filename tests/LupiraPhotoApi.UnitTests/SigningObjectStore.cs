using LupiraPhotoApi.Core.Storage;

namespace LupiraPhotoApi.UnitTests;

/// <summary>Mints a distinct URL on every presign, as SigV4 does once its timestamp moves.</summary>
public sealed class SigningObjectStore : IObjectStore
{
    public int Signed { get; private set; }

    public Task<Uri> PresignGetAsync(string key, TimeSpan expiry, CancellationToken ct = default) =>
        Task.FromResult(new Uri($"https://s3.test/{key}?sig={++Signed}"));

    public Task<Uri> PresignPutAsync(string key, string contentType, TimeSpan expiry, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<ObjectStat?> HeadAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<Stream> GetStreamAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();

    public Task PutAsync(string key, Stream content, long length, string contentType, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task DeleteAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();

    public Task EnsureBucketAsync(CancellationToken ct = default) => throw new NotSupportedException();
}
