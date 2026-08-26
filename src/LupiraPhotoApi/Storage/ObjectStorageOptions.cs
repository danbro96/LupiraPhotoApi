namespace LupiraPhotoApi.Storage;

/// <summary>Bound from the <c>ObjectStorage</c> section. Vendor-neutral S3 wire protocol — the deployed
/// backend is Garage, reached in-network for operations and presigned against the public hostname.</summary>
public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    /// <summary>In-network endpoint for PUT/GET/HEAD/DELETE (e.g. http://garage:3900).</summary>
    public string Endpoint { get; set; } = "http://localhost:3900";

    /// <summary>Public endpoint presigned URLs are minted against (e.g. https://s3.lupira.com) —
    /// SigV4 signs the host, so mobile clients must see the same hostname the signature carries.</summary>
    public string PublicEndpoint { get; set; } = "http://localhost:3900";

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string Bucket { get; set; } = "lupira-photo";

    /// <summary>Must match the store's configured region (Garage: <c>s3_region</c>) or signatures fail.</summary>
    public string Region { get; set; } = "garage";
}
