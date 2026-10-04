using System.Net;
using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Xunit;

namespace LupiraPhotoApi.IntegrationTests;

/// <summary>The same photo backed up from two phones. The surrogate stops it at declare; the worker's
/// hash catches what declared differently.</summary>
public class DuplicateTests(PhotoApiTestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task ASecondDeviceDeclaringTheSameFileIsToldNotToUpload()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();
        var canonical = await UploadFlowAsync(api, PhotoDeclare(bytes), bytes);

        var second = PhotoDeclare(bytes, mediaStoreId: "media-2");
        second.DeviceId = "device-2";
        var declared = await DeclareAsync(api, second);

        Assert.Equal(AssetStatus.Duplicate, declared.Status);
        Assert.Null(declared.UploadUrl);
        Assert.NotEqual(canonical, declared.AssetId);

        var dto = await api.GetFromJsonAsync<PhotoAssetDto>($"/photos/{declared.AssetId}", Json);
        Assert.Equal(canonical, dto!.DuplicateOfId);
    }

    [Fact]
    public async Task ADuplicateNeverReachesTheObjectStore()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();
        await UploadFlowAsync(api, PhotoDeclare(bytes), bytes);
        var before = Factory.S3.Objects.Count;

        var second = PhotoDeclare(bytes, mediaStoreId: "media-2");
        second.DeviceId = "device-2";
        await DeclareAsync(api, second);

        Assert.Equal(before, Factory.S3.Objects.Count);
    }

    /// <summary>Two devices whose MediaStore reports the capture time a second apart — the surrogate
    /// misses, so only the hash the worker computes can catch it.</summary>
    [Fact]
    public async Task IdenticalBytesDeclaredDifferentlyAreCaughtAfterProcessing()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();
        var canonical = await UploadFlowAsync(api, PhotoDeclare(bytes), bytes);
        await WaitForStatusAsync(api, canonical, AssetStatus.Ready);

        var second = PhotoDeclare(bytes, mediaStoreId: "media-2");
        second.DeviceId = "device-2";
        second.TakenAt = second.TakenAt.AddSeconds(1);
        var declared = await DeclareAsync(api, second);
        Assert.NotNull(declared.UploadUrl);
        await UploadAsync(declared, bytes);
        await CompleteAsync(api, declared.AssetId);

        var dto = await WaitForStatusAsync(api, declared.AssetId, AssetStatus.Duplicate);
        Assert.Equal(canonical, dto.DuplicateOfId);
        Assert.Null(dto.ThumbUrl);

        var original = await Factory.Store.QuerySession().LoadAsync<PhotoAsset>(declared.AssetId);
        Assert.False(Factory.S3.HasKey(original!.OriginalKey));
    }

    [Fact]
    public async Task DuplicatesAreHiddenFromTheGalleryUntilAskedForByName()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();
        var canonical = await UploadFlowAsync(api, PhotoDeclare(bytes), bytes);
        var second = PhotoDeclare(bytes, mediaStoreId: "media-2");
        second.DeviceId = "device-2";
        var duplicate = (await DeclareAsync(api, second)).AssetId;

        var listed = await api.GetFromJsonAsync<PhotoListResponse>("/photos", Json);
        Assert.Equal([canonical], listed!.Items.Select(i => i.Id));

        var duplicates = await api.GetFromJsonAsync<PhotoListResponse>("/photos?status=Duplicate", Json);
        Assert.Equal([duplicate], duplicates!.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task DeletingTheCanonicalTakesItsPointersWithIt()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();
        var canonical = await UploadFlowAsync(api, PhotoDeclare(bytes), bytes);
        var second = PhotoDeclare(bytes, mediaStoreId: "media-2");
        second.DeviceId = "device-2";
        var duplicate = (await DeclareAsync(api, second)).AssetId;

        var deleted = await api.DeleteAsync($"/photos/{canonical}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var orphan = await api.GetAsync($"/photos/{duplicate}");
        Assert.Equal(HttpStatusCode.NotFound, orphan.StatusCode);
    }

    /// <summary>A Declared asset may never be uploaded at all — and the janitor eventually expires it —
    /// so it must never become the canonical another asset points at.</summary>
    [Fact]
    public async Task ADeclaredAssetIsNotAdoptedAsTheCanonical()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();
        await DeclareAsync(api, PhotoDeclare(bytes));

        var second = PhotoDeclare(bytes, mediaStoreId: "media-2");
        second.DeviceId = "device-2";
        var declared = await DeclareAsync(api, second);

        Assert.Equal(AssetStatus.Declared, declared.Status);
        Assert.NotNull(declared.UploadUrl);
    }

    [Fact]
    public async Task AnotherPrincipalsIdenticalPhotoIsNotADuplicate()
    {
        var bytes = TinyJpeg();
        var anna = Factory.ApiClient("anna@example.com");
        await UploadFlowAsync(anna, PhotoDeclare(bytes), bytes);

        var bob = Factory.ApiClient("bob@example.com");
        var declared = await DeclareAsync(bob, PhotoDeclare(bytes));

        Assert.Equal(AssetStatus.Declared, declared.Status);
        Assert.NotNull(declared.UploadUrl);
    }
}
