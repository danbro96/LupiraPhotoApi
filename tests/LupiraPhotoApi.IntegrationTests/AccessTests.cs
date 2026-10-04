using System.Net;
using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Xunit;

namespace LupiraPhotoApi.IntegrationTests;

public class AccessTests(PhotoApiTestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Anonymous_IsRejectedEverywhere()
    {
        var anon = Factory.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/photos")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/photos", PhotoDeclare(TinyJpeg()), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/photos/map?bbox=0,0,1,1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/me")).StatusCode);
    }

    [Fact]
    public async Task CrossUser_AssetsAreInvisible()
    {
        var anna = Factory.ApiClient("anna@example.com");
        var erik = Factory.ApiClient("erik@example.com");
        var bytes = TinyJpeg();
        var id = await UploadFlowAsync(anna, PhotoDeclare(bytes), bytes);
        await WaitForStatusAsync(anna, id, AssetStatus.Ready);

        Assert.Empty((await erik.GetFromJsonAsync<PhotoListResponse>("/photos", Json))!.Items);
        Assert.Empty((await erik.GetFromJsonAsync<PhotoMapResponse>("/photos/map?bbox=17,58,19,60", Json))!.Features);
        Assert.Equal(HttpStatusCode.NotFound, (await erik.GetAsync($"/photos/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await erik.DeleteAsync($"/photos/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await erik.PostAsync($"/photos/{id}/reprocess", null)).StatusCode);

        // Anna still owns it.
        Assert.Equal(HttpStatusCode.OK, (await anna.GetAsync($"/photos/{id}")).StatusCode);
    }

    [Fact]
    public async Task ListPaging_WalksTheCursor()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();
        for (var i = 0; i < 5; i++)
        {
            var request = PhotoDeclare(bytes, mediaStoreId: $"media-{i}");
            request.TakenAt = new DateTimeOffset(2026, 8, 1 + i, 12, 0, 0, TimeSpan.Zero);
            await UploadFlowAsync(api, request, bytes);
        }

        var page1 = await api.GetFromJsonAsync<PhotoListResponse>("/photos?limit=2", Json);
        Assert.Equal(2, page1!.Items.Count);
        Assert.NotNull(page1.NextCursor);

        var page2 = await api.GetFromJsonAsync<PhotoListResponse>($"/photos?limit=2&cursor={Uri.EscapeDataString(page1.NextCursor!)}", Json);
        var page3 = await api.GetFromJsonAsync<PhotoListResponse>($"/photos?limit=2&cursor={Uri.EscapeDataString(page2!.NextCursor!)}", Json);
        Assert.Single(page3!.Items);
        Assert.Null(page3.NextCursor);

        var all = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(i => i.Id).ToList();
        Assert.Equal(5, all.Distinct().Count());
        // Newest first across pages.
        var takenAts = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(i => i.TakenAt).ToList();
        Assert.Equal(takenAts.OrderByDescending(t => t), takenAts);
    }

    [Fact]
    public async Task MalformedCursorAndBbox_Are400()
    {
        var api = Factory.ApiClient("anna@example.com");
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("/photos?cursor=%21%21%21")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("/photos/map?bbox=nope")).StatusCode);
    }
}
