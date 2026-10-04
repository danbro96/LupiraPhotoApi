using System.Net;
using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraPhotoApi.Core.Application.Processing;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Marten;
using Xunit;

namespace LupiraPhotoApi.IntegrationTests;

/// <summary>Hand-set capture times: set, clear, and retime by selector.</summary>
public class PhotoTimeTests(PhotoApiTestFactory factory) : IntegrationTest(factory)
{
    private static readonly DateTimeOffset Day = new(2011, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private async Task<PhotoAsset> LoadAsync(Guid id)
    {
        await using var session = Factory.Store.QuerySession();
        return (await session.LoadAsync<PhotoAsset>(id))!;
    }

    private static PhotoAsset Shot(Guid owner, string model, DateTimeOffset takenAt, string deviceId = "import:handelser")
    {
        var asset = Seeded(owner);
        asset.Camera = new CameraInfo { Make = "HTC", Model = model };
        asset.TakenAt = takenAt;
        asset.TakenAtSource = TakenAtSource.Exif;
        asset.DeviceId = deviceId;
        return asset;
    }

    [Fact]
    public async Task SetTakenAt_ThenClear_RestoresTheDerivedTime()
    {
        var api = Factory.ApiClient("anna@example.com");
        var asset = Shot(await PrincipalIdAsync(api), "HTC Desire", Day);
        await StoreAsync(asset);

        var set = await api.PutAsJsonAsync($"/photos/{asset.Id}/taken-at", new SetPhotoTakenAtRequest { TakenAt = Day.AddHours(-1).ToOffset(TimeSpan.FromHours(2)) }, Json);
        set.EnsureSuccessStatusCode();
        var manual = (await set.Content.ReadFromJsonAsync<PhotoAssetDto>(Json))!;
        Assert.Equal((TakenAtSource.Manual, Day.AddHours(-1)), (manual.TakenAtSource, manual.TakenAt));

        var cleared = await api.DeleteAsync($"/photos/{asset.Id}/taken-at");
        cleared.EnsureSuccessStatusCode();
        var restored = (await cleared.Content.ReadFromJsonAsync<PhotoAssetDto>(Json))!;
        Assert.Equal((TakenAtSource.Exif, Day), (restored.TakenAtSource, restored.TakenAt));
    }

    [Fact]
    public async Task Retime_ShiftsOneCamerasWindow_AndDryRunChangesNothing()
    {
        var api = Factory.ApiClient("anna@example.com");
        var owner = await PrincipalIdAsync(api);
        var first = Shot(owner, "HTC Desire", Day);
        var second = Shot(owner, "HTC Desire", Day.AddMinutes(30));
        var otherCamera = Shot(owner, "Nexus 5", Day);
        var otherDevice = Shot(owner, "HTC Desire", Day, deviceId: "device-1");
        var outsideWindow = Shot(owner, "HTC Desire", Day.AddDays(2));
        await StoreAsync(first, second, otherCamera, otherDevice, outsideWindow);

        var request = new RetimePhotosRequest
        {
            ShiftBy = TimeSpan.FromHours(-1),
            From = Day.AddHours(-1),
            To = Day.AddHours(1),
            CameraModel = "htc desire",
            DeviceId = "import:handelser",
            DryRun = true,
        };
        var preview = await PostRetimeAsync(api, request);
        Assert.Equal([(first.Id, Day, Day.AddHours(-1)), (second.Id, Day.AddMinutes(30), Day.AddMinutes(-30))], preview.Items.Select(i => (i.Id, i.Before, i.After)));
        Assert.Equal(TakenAtSource.Exif, (await LoadAsync(first.Id)).TakenAtSource);

        request.DryRun = false;
        Assert.Equal(2, (await PostRetimeAsync(api, request)).Count);

        Assert.Equal((TakenAtSource.Manual, Day.AddHours(-1)), ((await LoadAsync(first.Id)).TakenAtSource, (await LoadAsync(first.Id)).TakenAt));
        foreach (var untouched in new[] { otherCamera, otherDevice, outsideWindow })
            Assert.Equal(TakenAtSource.Exif, (await LoadAsync(untouched.Id)).TakenAtSource);
    }

    [Fact]
    public async Task Retime_RequeuesAHistoryGeotag_SoItFollowsTheNewTime()
    {
        var api = Factory.ApiClient("anna@example.com");
        var takenAt = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
        Factory.History.HitAt = ts => ts < takenAt.AddMinutes(30)
            ? new LocationHistoryHit { Latitude = 59.33, Longitude = 18.07, Label = "Stockholm" }
            : new LocationHistoryHit { Latitude = 57.70, Longitude = 11.97, Label = "Göteborg" };
        var noGps = TinyJpeg(1);
        var historyId = await UploadFlowAsync(api, PhotoDeclare(noGps, mediaStoreId: "no-gps", lat: null, lon: null), noGps);
        var gps = TinyJpeg(2);
        var gpsId = await UploadFlowAsync(api, PhotoDeclare(gps, mediaStoreId: "gps"), gps);
        Assert.Equal("Stockholm", (await WaitForStatusAsync(api, historyId, AssetStatus.Ready)).PlaceLabel);
        await WaitForStatusAsync(api, gpsId, AssetStatus.Ready);

        await PostRetimeAsync(api, new RetimePhotosRequest { Ids = [historyId, gpsId], ShiftBy = TimeSpan.FromHours(1) });

        Assert.Equal(AssetStatus.Ready, (await LoadAsync(gpsId)).Status);
        var moved = await WaitForStatusAsync(api, historyId, AssetStatus.Ready);
        Assert.Equal((GeotagSource.LocationHistory, "Göteborg", 57.70), (moved.GeotagSource, moved.PlaceLabel, moved.Latitude!.Value));
        Assert.Equal(takenAt.AddHours(1), moved.TakenAt);
    }

    [Fact]
    public async Task Retime_OfANamedDuplicate_IsAConflict()
    {
        var api = Factory.ApiClient("anna@example.com");
        var duplicate = Shot(await PrincipalIdAsync(api), "HTC Desire", Day);
        duplicate.Status = AssetStatus.Duplicate;
        await StoreAsync(duplicate);

        var response = await api.PostAsJsonAsync("/photos/retime", new RetimePhotosRequest { Ids = [duplicate.Id], SetTo = Day }, Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Retime_NeedsExactlyOneOperation(bool shift, bool set)
    {
        var api = Factory.ApiClient("anna@example.com");
        var request = new RetimePhotosRequest
        {
            Ids = [Guid.NewGuid()],
            ShiftBy = shift ? TimeSpan.FromHours(1) : null,
            SetTo = set ? Day : null,
        };

        var response = await api.PostAsJsonAsync("/photos/retime", request, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<RetimePhotosResponse> PostRetimeAsync(HttpClient api, RetimePhotosRequest request)
    {
        var response = await api.PostAsJsonAsync("/photos/retime", request, Json);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RetimePhotosResponse>(Json))!;
    }
}
