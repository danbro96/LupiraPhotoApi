using System.Net;
using System.Net.Http.Json;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Xunit;

namespace LupiraPhotoApi.IntegrationTests;

public class PhotoFlowTests(PhotoApiTestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task DeclareUploadCompleteProcess_EndsReady_WithThumbAndGeotag()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();

        var declared = await DeclareAsync(api, PhotoDeclare(bytes));
        Assert.Equal(AssetStatus.Declared, declared.Status);
        Assert.NotNull(declared.UploadUrl);
        Assert.Equal("image/jpeg", declared.RequiredHeaders["Content-Type"]);

        await UploadAsync(declared, bytes);
        var completed = await CompleteAsync(api, declared.AssetId);
        Assert.Equal(AssetStatus.Uploaded, completed.Status);

        var ready = await WaitForStatusAsync(api, declared.AssetId, AssetStatus.Ready);
        Assert.Equal(GeotagSource.ExifGps, ready.GeotagSource);
        Assert.Equal("Testville", ready.PlaceLabel);
        Assert.NotNull(ready.ThumbUrl);
        Assert.NotNull(ready.OriginalUrl);

        // The real Magick pipeline wrote a WebP thumb into the store.
        Assert.Contains(Factory.S3.Objects.Keys, k => k.StartsWith("thumbs/"));

        // Presigned GET actually serves the original bytes.
        using var http = new HttpClient();
        Assert.Equal(bytes, await http.GetByteArrayAsync(ready.OriginalUrl));
    }

    [Fact]
    public async Task List_And_Map_ReturnTheAsset()
    {
        var api = Factory.ApiClient("anna@example.com");
        var id = await UploadFlowAsync(api, PhotoDeclare(TinyJpeg()), TinyJpeg());
        await WaitForStatusAsync(api, id, AssetStatus.Ready);

        var list = await api.GetFromJsonAsync<PhotoListResponse>("/photos", Json);
        var item = Assert.Single(list!.Items);
        Assert.Equal(id, item.Id);
        Assert.NotNull(item.ThumbUrl);
        Assert.Null(list.NextCursor);

        var map = await api.GetFromJsonAsync<PhotoMapResponse>("/photos/map?bbox=17,58,19,60", Json);
        var feature = Assert.Single(map!.Features);
        Assert.Equal(id, feature.Properties.Id);
        Assert.Equal(18.07, feature.Geometry.Coordinates[0]);
        Assert.Equal(59.33, feature.Geometry.Coordinates[1]);

        var outside = await api.GetFromJsonAsync<PhotoMapResponse>("/photos/map?bbox=0,0,1,1", Json);
        Assert.Empty(outside!.Features);
    }

    [Fact]
    public async Task Video_UsesStubThumbnailer_AndBackfillsDimensions()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var request = PhotoDeclare(bytes, mediaStoreId: "vid-1");
        request.ContentType = "video/mp4";
        request.Width = null;
        request.Height = null;
        request.DurationSeconds = 12.5;

        var id = await UploadFlowAsync(api, request, bytes);
        var ready = await WaitForStatusAsync(api, id, AssetStatus.Ready);
        Assert.Equal(AssetKind.Video, ready.Kind);
        Assert.Equal(1920, ready.Width);
        Assert.Equal(1080, ready.Height);
        Assert.Equal(12.5, ready.DurationSeconds);
    }

    [Fact]
    public async Task NoGps_FallsBackToLocationHistory()
    {
        Factory.History.Hit = new LupiraPhotoApi.Core.Application.Processing.LocationHistoryHit
        {
            Latitude = 56.05,
            Longitude = 14.15,
            Label = "Home",
        };
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();
        var id = await UploadFlowAsync(api, PhotoDeclare(bytes, lat: null, lon: null), bytes);

        var ready = await WaitForStatusAsync(api, id, AssetStatus.Ready);
        Assert.Equal(GeotagSource.LocationHistory, ready.GeotagSource);
        Assert.Equal(56.05, ready.Latitude);
        Assert.Equal("Home", ready.PlaceLabel);
    }

    [Fact]
    public async Task NoGps_NoHistory_EndsReadyWithoutGeotag()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();
        var id = await UploadFlowAsync(api, PhotoDeclare(bytes, lat: null, lon: null), bytes);

        var ready = await WaitForStatusAsync(api, id, AssetStatus.Ready);
        Assert.Equal(GeotagSource.None, ready.GeotagSource);
        Assert.Null(ready.Latitude);
    }

    [Fact]
    public async Task Reprocess_RunsThePipelineAgain()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();
        var id = await UploadFlowAsync(api, PhotoDeclare(bytes), bytes);
        await WaitForStatusAsync(api, id, AssetStatus.Ready);

        Factory.Geo.Label = "Renamed";
        var resp = await api.PostAsync($"/photos/{id}/reprocess", null);
        resp.EnsureSuccessStatusCode();

        var ready = await WaitForStatusAsync(api, id, AssetStatus.Ready);
        Assert.Equal("Renamed", ready.PlaceLabel);
    }

    [Fact]
    public async Task Delete_RemovesObjectsAndDocument()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();
        var id = await UploadFlowAsync(api, PhotoDeclare(bytes), bytes);
        await WaitForStatusAsync(api, id, AssetStatus.Ready);
        Assert.NotEmpty(Factory.S3.Objects);

        var resp = await api.DeleteAsync($"/photos/{id}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.Empty(Factory.S3.Objects);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/photos/{id}")).StatusCode);
    }
}
