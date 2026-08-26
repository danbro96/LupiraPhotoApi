using LupiraPhotoApi.Core.Application.Processing;
using Marten;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace LupiraPhotoApi.IntegrationTests;

/// <summary>ffmpeg stays out of CI — video posters are a fixed WebP payload.</summary>
public sealed class StubVideoThumbnailer : IVideoThumbnailer
{
    public Task<ThumbnailResult> CreateAsync(string sourcePath, CancellationToken ct = default) =>
        Task.FromResult(new ThumbnailResult { WebpBytes = [0x52, 0x49, 0x46, 0x46], SourceWidth = 1920, SourceHeight = 1080 });
}
