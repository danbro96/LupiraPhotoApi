using System.Net;
using System.Net.Http.Json;
using LupiraPhotoApi.Domain;
using LupiraPhotoApi.Dtos.Photos;
using Xunit;

namespace LupiraPhotoApi.IntegrationTests;

public class DeclareContractTests(PhotoApiTestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Redeclare_IsIdempotent_SameAssetFreshUrl()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();

        var first = await DeclareAsync(api, PhotoDeclare(bytes));
        var second = await DeclareAsync(api, PhotoDeclare(bytes));
        Assert.Equal(first.AssetId, second.AssetId);
        Assert.Equal(AssetStatus.Declared, second.Status);
        Assert.NotNull(second.UploadUrl);
    }

    [Fact]
    public async Task Redeclare_AfterUpload_ReturnsStatusWithoutUrl()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();
        var id = await UploadFlowAsync(api, PhotoDeclare(bytes), bytes);

        var redeclared = await DeclareAsync(api, PhotoDeclare(bytes));
        Assert.Equal(id, redeclared.AssetId);
        Assert.NotEqual(AssetStatus.Declared, redeclared.Status);
        Assert.Null(redeclared.UploadUrl);
    }

    [Fact]
    public async Task SameMediaStoreId_DifferentUsers_AreDifferentAssets()
    {
        var bytes = TinyJpeg();
        var anna = await DeclareAsync(Factory.ApiClient("anna@example.com"), PhotoDeclare(bytes));
        var erik = await DeclareAsync(Factory.ApiClient("erik@example.com"), PhotoDeclare(bytes));
        Assert.NotEqual(anna.AssetId, erik.AssetId);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/svg+xml")]
    public async Task UnsupportedContentType_Is400(string contentType)
    {
        var api = Factory.ApiClient("anna@example.com");
        var request = PhotoDeclare(TinyJpeg());
        request.ContentType = contentType;
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync("/photos", request, Json)).StatusCode);
    }

    [Fact]
    public async Task OversizedDeclare_Is400()
    {
        var api = Factory.ApiClient("anna@example.com");
        var request = PhotoDeclare(TinyJpeg());
        request.SizeBytes = 9L * 1024 * 1024 * 1024;
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync("/photos", request, Json)).StatusCode);
    }

    [Fact]
    public async Task Complete_WithoutUpload_Is409_AndStaysDeclared()
    {
        var api = Factory.ApiClient("anna@example.com");
        var declared = await DeclareAsync(api, PhotoDeclare(TinyJpeg()));

        Assert.Equal(HttpStatusCode.Conflict, (await api.PostAsync($"/photos/{declared.AssetId}/complete", null)).StatusCode);

        var redeclared = await DeclareAsync(api, PhotoDeclare(TinyJpeg()));
        Assert.Equal(AssetStatus.Declared, redeclared.Status);
    }

    [Fact]
    public async Task Complete_WithSizeMismatch_Is409()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();
        var request = PhotoDeclare(bytes);
        request.SizeBytes = bytes.Length + 10;

        var declared = await DeclareAsync(api, request);
        await UploadAsync(declared, bytes);
        Assert.Equal(HttpStatusCode.Conflict, (await api.PostAsync($"/photos/{declared.AssetId}/complete", null)).StatusCode);
    }

    [Fact]
    public async Task Complete_IsIdempotent()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();
        var id = await UploadFlowAsync(api, PhotoDeclare(bytes), bytes);

        var again = await CompleteAsync(api, id);
        Assert.NotEqual(AssetStatus.Declared, again.Status);
    }
}
