using System.Net;
using System.Net.Http.Json;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Marten;
using Xunit;

namespace LupiraPhotoApi.IntegrationTests;

/// <summary>Hand-set locations: they outrank the file's GPS, survive reprocessing, and clearing re-derives.</summary>
public class PhotoLocationTests(PhotoApiTestFactory factory) : IntegrationTest(factory)
{
    private const double StaleLat = 55.390556;
    private const double StaleLon = 12.833056;
    private static readonly DateTimeOffset Day = new(2015, 8, 9, 9, 0, 0, TimeSpan.Zero);

    private async Task<PhotoAsset> LoadAsync(Guid id)
    {
        await using var session = Factory.Store.QuerySession();
        return (await session.LoadAsync<PhotoAsset>(id))!;
    }

    private static async Task<RelocatePhotosResponse> RelocateAsync(HttpClient api, RelocatePhotosRequest request)
    {
        var response = await api.PostAsJsonAsync("/photos/relocate", request, Json);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RelocatePhotosResponse>(Json))!;
    }

    private static PhotoAsset Shot(Guid owner, string model, double lat, double lon, DateTimeOffset takenAt)
    {
        var asset = Located(owner, lat, lon, takenAt);
        asset.Camera = new CameraInfo { Make = "SAMSUNG", Model = model };
        return asset;
    }

    [Fact]
    public async Task SetLocation_SurvivesReprocess_AndClearingRestoresTheFileFix()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();
        var id = await UploadFlowAsync(api, PhotoDeclare(bytes), bytes);
        await WaitForStatusAsync(api, id, AssetStatus.Ready);

        var set = await api.PutAsJsonAsync($"/photos/{id}/location", new SetPhotoLocationRequest { Latitude = 59.02, Longitude = 16.47, Label = "Mormor" }, Json);
        set.EnsureSuccessStatusCode();
        var manual = (await set.Content.ReadFromJsonAsync<PhotoAssetDto>(Json))!;
        Assert.Equal(GeotagSource.Manual, manual.GeotagSource);
        Assert.Equal("Mormor", manual.PlaceLabel);

        (await api.PostAsync($"/photos/{id}/reprocess", null)).EnsureSuccessStatusCode();
        var reprocessed = await WaitForStatusAsync(api, id, AssetStatus.Ready);
        Assert.Equal(GeotagSource.Manual, reprocessed.GeotagSource);
        Assert.Equal(59.02, reprocessed.Latitude);
        Assert.Equal("Mormor", reprocessed.PlaceLabel);

        (await api.DeleteAsync($"/photos/{id}/location")).EnsureSuccessStatusCode();
        var cleared = await WaitForStatusAsync(api, id, AssetStatus.Ready);
        Assert.Equal(GeotagSource.ExifGps, cleared.GeotagSource);
        Assert.Equal(59.33, cleared.Latitude);
        Assert.Equal(18.07, cleared.Longitude);
    }

    [Fact]
    public async Task Relocate_MovesOneCamerasStaleFix_InTheWindow_Only()
    {
        var api = Factory.ApiClient("anna@example.com");
        var owner = await PrincipalIdAsync(api);
        var stale = Shot(owner, "GT-I9300", StaleLat, StaleLon, Day);
        var staleToo = Shot(owner, "GT-I9300", StaleLat, StaleLon, Day.AddMinutes(12));
        var realFix = Shot(owner, "GT-I9300", 59.016, 16.47, Day.AddMinutes(1));
        var otherCamera = Shot(owner, "G8341", StaleLat, StaleLon, Day);
        var outsideWindow = Shot(owner, "GT-I9300", StaleLat, StaleLon, Day.AddDays(30));
        var duplicate = Shot(owner, "GT-I9300", StaleLat, StaleLon, Day);
        duplicate.Status = AssetStatus.Duplicate;
        await StoreAsync(stale, staleToo, realFix, otherCamera, outsideWindow, duplicate);

        var request = new RelocatePhotosRequest
        {
            Latitude = 59.02,
            Longitude = 16.47,
            // A local-time window, as a client in Sweden sends it.
            From = Day.AddHours(-1).ToOffset(TimeSpan.FromHours(2)),
            To = Day.AddHours(1).ToOffset(TimeSpan.FromHours(2)),
            CameraModel = "gt-i9300",
            AtLatitude = StaleLat,
            AtLongitude = StaleLon,
            DryRun = true,
        };
        var preview = await RelocateAsync(api, request);
        Assert.Equal([stale.Id, staleToo.Id], preview.Ids);
        Assert.Equal(GeotagSource.ExifGps, (await LoadAsync(stale.Id)).GeotagSource);

        request.DryRun = false;
        var moved = await RelocateAsync(api, request);
        Assert.Equal(2, moved.Count);
        Assert.Equal("Testville", moved.PlaceLabel);

        foreach (var id in moved.Ids)
        {
            var asset = await LoadAsync(id);
            Assert.Equal(GeotagSource.Manual, asset.GeotagSource);
            Assert.Equal((59.02, 16.47), (asset.Latitude!.Value, asset.Longitude!.Value));
        }

        foreach (var untouched in new[] { realFix, otherCamera, outsideWindow, duplicate })
            Assert.Equal(GeotagSource.ExifGps, (await LoadAsync(untouched.Id)).GeotagSource);
    }

    [Fact]
    public async Task Relocate_WithoutIdsOrAWindow_IsRejected()
    {
        var api = Factory.ApiClient("anna@example.com");
        var response = await api.PostAsJsonAsync("/photos/relocate", new RelocatePhotosRequest { Latitude = 59.02, Longitude = 16.47, CameraModel = "GT-I9300" }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetLocation_OnSomeoneElsesAsset_IsNotFound()
    {
        var anna = Factory.ApiClient("anna@example.com");
        var erik = Factory.ApiClient("erik@example.com");
        var asset = Shot(await PrincipalIdAsync(anna), "GT-I9300", StaleLat, StaleLon, Day);
        await StoreAsync(asset);

        var response = await erik.PutAsJsonAsync($"/photos/{asset.Id}/location", new SetPhotoLocationRequest { Latitude = 59.02, Longitude = 16.47 }, Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(GeotagSource.ExifGps, (await LoadAsync(asset.Id)).GeotagSource);
    }
}
