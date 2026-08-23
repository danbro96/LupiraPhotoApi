using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using LupiraPhotoApi.Storage;
using Microsoft.Extensions.Options;

namespace LupiraPhotoApi.Storage;

/// <summary>AWSSDK.S3 implementation over Garage. Two clients on the same credentials: operations run
/// against the in-network endpoint; presigning runs against the public endpoint (SigV4 signs the host,
/// and signing is offline — no request ever leaves the presign client).</summary>
public sealed class S3ObjectStore : IObjectStore, IDisposable
{
    private readonly AmazonS3Client _ops;
    private readonly AmazonS3Client _presign;
    private readonly Amazon.S3.Protocol _presignProtocol;
    private readonly string _bucket;

    public S3ObjectStore(IOptions<ObjectStorageOptions> options)
    {
        var opts = options.Value;
        var credentials = new BasicAWSCredentials(opts.AccessKey, opts.SecretKey);
        _ops = new AmazonS3Client(credentials, Config(opts.Endpoint, opts.Region));
        _presign = new AmazonS3Client(credentials, Config(opts.PublicEndpoint, opts.Region));
        _presignProtocol = opts.PublicEndpoint.StartsWith("https", StringComparison.OrdinalIgnoreCase)
            ? Amazon.S3.Protocol.HTTPS
            : Amazon.S3.Protocol.HTTP;
        _bucket = opts.Bucket;
    }

    private static AmazonS3Config Config(string serviceUrl, string region) => new()
    {
        ServiceURL = serviceUrl,
        ForcePathStyle = true,
        AuthenticationRegion = region,
        // SDK v4 defaults to CRC checksum trailers that third-party S3 backends reject.
        RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
    };

    public async Task<Uri> PresignPutAsync(string key, string contentType, TimeSpan expiry, CancellationToken ct = default)
    {
        var url = await _presign.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            Protocol = _presignProtocol,
            Expires = DateTime.UtcNow + expiry,
            ContentType = contentType,
        });
        return new Uri(url);
    }

    public async Task<Uri> PresignGetAsync(string key, TimeSpan expiry, CancellationToken ct = default)
    {
        var url = await _presign.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Protocol = _presignProtocol,
            Expires = DateTime.UtcNow + expiry,
        });
        return new Uri(url);
    }

    public async Task<ObjectStat?> HeadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var meta = await _ops.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = _bucket, Key = key }, ct);
            return new ObjectStat
            {
                SizeBytes = meta.ContentLength,
                ContentType = meta.Headers.ContentType,
                ETag = meta.ETag,
            };
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Stream> GetStreamAsync(string key, CancellationToken ct = default)
    {
        var response = await _ops.GetObjectAsync(new GetObjectRequest { BucketName = _bucket, Key = key }, ct);
        return response.ResponseStream;
    }

    public Task PutAsync(string key, Stream content, long length, string contentType, CancellationToken ct = default) =>
        _ops.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            AutoCloseStream = false,
            ContentType = contentType,
            // Content-length'd single-shot body — no aws-chunked encoding for third-party compatibility.
            UseChunkEncoding = false,
        }, ct);

    public Task DeleteAsync(string key, CancellationToken ct = default) =>
        _ops.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucket, Key = key }, ct);

    public async Task EnsureBucketAsync(CancellationToken ct = default)
    {
        // Keys are least-privilege (no bucket creation) — existence check only, with a pointed error.
        try
        {
            await _ops.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _bucket, MaxKeys = 1 }, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Bucket '{_bucket}' not found — create it via the garage CLI (see deploy docs).", ex);
        }
    }

    public void Dispose()
    {
        _ops.Dispose();
        _presign.Dispose();
    }
}
