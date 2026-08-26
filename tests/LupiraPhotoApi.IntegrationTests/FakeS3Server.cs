using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LupiraPhotoApi.IntegrationTests;

/// <summary>
/// In-process path-style S3 fake on a real Kestrel port: PUT/GET/HEAD/DELETE on <c>/{bucket}/{key}</c>
/// plus a minimal ListObjectsV2 for the bucket readiness check. Ignores SigV4 (presigned query params
/// ride along untouched), which is exactly what makes the full declare → PUT → complete → process flow
/// exercisable without a Garage container.
/// </summary>
public sealed class FakeS3Server : IAsyncDisposable
{
    private readonly WebApplication _app;

    public ConcurrentDictionary<string, (byte[] Bytes, string? ContentType)> Objects { get; } = new();

    public string BaseUrl { get; }

    public FakeS3Server()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();

        _app.Run(async ctx =>
        {
            var path = ctx.Request.Path.Value?.TrimStart('/') ?? "";
            var slash = path.IndexOf('/');

            // Bucket-level GET (ListObjectsV2) — the readiness/EnsureBucket probe.
            if (slash < 0)
            {
                if (ctx.Request.Method == "GET" && path.Length > 0)
                {
                    ctx.Response.ContentType = "application/xml";
                    await ctx.Response.WriteAsync(
                        $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><ListBucketResult><Name>{path}</Name><KeyCount>0</KeyCount><IsTruncated>false</IsTruncated></ListBucketResult>");
                    return;
                }
                ctx.Response.StatusCode = 404;
                return;
            }

            var key = path[(slash + 1)..];
            switch (ctx.Request.Method)
            {
                case "PUT":
                    {
                        using var buffer = new MemoryStream();
                        await ctx.Request.Body.CopyToAsync(buffer);
                        Objects[key] = (buffer.ToArray(), ctx.Request.ContentType);
                        ctx.Response.Headers.ETag = "\"fake\"";
                        break;
                    }
                case "HEAD" when Objects.TryGetValue(key, out var head):
                    ctx.Response.ContentLength = head.Bytes.Length;
                    ctx.Response.ContentType = head.ContentType ?? "application/octet-stream";
                    ctx.Response.Headers.ETag = "\"fake\"";
                    break;
                case "GET" when Objects.TryGetValue(key, out var get):
                    ctx.Response.ContentLength = get.Bytes.Length;
                    ctx.Response.ContentType = get.ContentType ?? "application/octet-stream";
                    await ctx.Response.Body.WriteAsync(get.Bytes);
                    break;
                case "DELETE":
                    Objects.TryRemove(key, out _);
                    ctx.Response.StatusCode = 204;
                    break;
                default:
                    ctx.Response.StatusCode = 404;
                    break;
            }
        });

        _app.Start();
        BaseUrl = _app.Urls.First();
    }

    /// <summary>Objects are stored under the object key alone — the first path segment (bucket) is stripped.</summary>
    public bool HasKey(string objectKey) => Objects.ContainsKey(objectKey);

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
