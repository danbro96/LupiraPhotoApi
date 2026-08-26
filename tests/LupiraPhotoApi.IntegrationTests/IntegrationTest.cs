using System.Net.Http.Json;
using ImageMagick;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Xunit;

namespace LupiraPhotoApi.IntegrationTests;

/// <summary>Base for integration tests: shares the container fixture, resets all state before each test,
/// and provides declare/upload/complete helpers. Serial within the "integration" collection.</summary>
[Collection("integration")]
public abstract class IntegrationTest(PhotoApiTestFactory factory) : IAsyncLifetime
{
    /// <summary>Mirrors the API's wire options (camelCase + string enums).</summary>
    protected static readonly System.Text.Json.JsonSerializerOptions Json =
        new(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

    protected readonly PhotoApiTestFactory Factory = factory;

    public async Task InitializeAsync() => await Factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    protected static byte[] TinyJpeg()
    {
        using var image = new MagickImage(MagickColors.CornflowerBlue, 32, 24);
        image.Format = MagickFormat.Jpeg;
        return image.ToByteArray();
    }

    protected static DeclarePhotoRequest PhotoDeclare(byte[] bytes, string mediaStoreId = "media-1", double? lat = 59.33, double? lon = 18.07) => new()
    {
        DeviceId = "device-1",
        MediaStoreId = mediaStoreId,
        ContentType = "image/jpeg",
        SizeBytes = bytes.Length,
        TakenAt = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero),
        Latitude = lat,
        Longitude = lon,
        Width = 32,
        Height = 24,
    };

    protected static async Task<DeclaredPhotoResponse> DeclareAsync(HttpClient api, DeclarePhotoRequest request)
    {
        var resp = await api.PostAsJsonAsync("/photos", request, Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<DeclaredPhotoResponse>(Json))!;
    }

    protected static async Task UploadAsync(DeclaredPhotoResponse declared, byte[] bytes)
    {
        using var http = new HttpClient();
        using var content = new ByteArrayContent(bytes);
        foreach (var (name, value) in declared.RequiredHeaders)
            content.Headers.TryAddWithoutValidation(name, value);
        var resp = await http.PutAsync(declared.UploadUrl, content);
        resp.EnsureSuccessStatusCode();
    }

    protected static async Task<PhotoAssetDto> CompleteAsync(HttpClient api, Guid assetId)
    {
        var resp = await api.PostAsync($"/photos/{assetId}/complete", null);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<PhotoAssetDto>(Json))!;
    }

    /// <summary>Full declare → presigned PUT → complete round trip.</summary>
    protected static async Task<Guid> UploadFlowAsync(HttpClient api, DeclarePhotoRequest request, byte[] bytes)
    {
        var declared = await DeclareAsync(api, request);
        await UploadAsync(declared, bytes);
        await CompleteAsync(api, declared.AssetId);
        return declared.AssetId;
    }

    /// <summary>Polls the asset until the background worker lands it on <paramref name="status"/>.</summary>
    protected static async Task<PhotoAssetDto> WaitForStatusAsync(HttpClient api, Guid assetId, AssetStatus status, int timeoutSeconds = 20)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var dto = await api.GetFromJsonAsync<PhotoAssetDto>($"/photos/{assetId}", Json);
            if (dto!.Status == status) return dto;
            if (dto.Status == AssetStatus.Failed && status != AssetStatus.Failed)
                Assert.Fail($"Asset failed: {dto.LastError}");
            await Task.Delay(250);
        }
        throw new TimeoutException($"Asset {assetId} did not reach {status} within {timeoutSeconds}s.");
    }
}
