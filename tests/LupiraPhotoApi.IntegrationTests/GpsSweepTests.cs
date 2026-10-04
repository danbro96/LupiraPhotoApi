using System.Net.Http.Json;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Xunit;

namespace LupiraPhotoApi.IntegrationTests;

/// <summary>The impossible-GPS sweep: a dry run reports, apply rejects spikes and named repeats, restore undoes.</summary>
public class GpsSweepTests(PhotoApiTestFactory factory) : IntegrationTest(factory)
{
    private const double FalsterboLat = 55.390556;
    private const double FalsterboLon = 12.833056;
    private const string FalsterboBbox = "12.8,55.38,12.86,55.40";
    private static readonly DateTimeOffset Day = new(2015, 8, 9, 9, 0, 0, TimeSpan.Zero);

    private static async Task<GpsSweepResponse> SweepAsync(HttpClient api, GpsSweepRequest request)
    {
        var response = await api.PostAsJsonAsync("/photos/gps-sweep", request, Json);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<GpsSweepResponse>(Json))!;
    }

    private static async Task<int> FalsterboPinsAsync(HttpClient api) =>
        (await api.GetFromJsonAsync<PhotoMapResponse>($"/photos/map?bbox={FalsterboBbox}&zoom=17", Json))!.Features.Count;

    private static async Task<Guid> PhoneShotAsync(HttpClient api, int variant, DateTimeOffset takenAt, double lat, double lon)
    {
        var bytes = TinyJpeg(variant);
        var declare = PhotoDeclare(bytes, mediaStoreId: $"media-{variant}", lat: lat, lon: lon);
        declare.TakenAt = takenAt;
        var id = await UploadFlowAsync(api, declare, bytes);
        await WaitForStatusAsync(api, id, AssetStatus.Ready);
        return id;
    }

    [Fact]
    public async Task Sweep_RejectsTheSpike_ThenANamedRepeat_AndRestoreBringsItBack()
    {
        var api = Factory.ApiClient("anna@example.com");
        await PhoneShotAsync(api, 0, Day, 59.33, 18.07);
        var spike = await PhoneShotAsync(api, 1, Day.AddMinutes(5), FalsterboLat, FalsterboLon);
        await PhoneShotAsync(api, 2, Day.AddMinutes(10), 59.331, 18.071);
        var stale = await PhoneShotAsync(api, 3, Day.AddDays(3), FalsterboLat, FalsterboLon);

        var report = await SweepAsync(api, new GpsSweepRequest());
        Assert.Equal((4, 1, 0), (report.Scanned, report.SpikeCount, report.Rejected));
        Assert.Equal(spike, Assert.Single(report.Spikes).Id);
        var repeat = Assert.Single(report.Repeats);
        Assert.Equal((FalsterboLat, FalsterboLon, 2, 2), (repeat.Latitude, repeat.Longitude, repeat.Count, repeat.Days));
        Assert.Equal(2, await FalsterboPinsAsync(api));

        var applied = await SweepAsync(api, new GpsSweepRequest { Apply = true });
        Assert.Equal(1, applied.Rejected);
        Assert.Equal([spike], applied.RejectedIds);
        var rejected = await WaitForStatusAsync(api, spike, AssetStatus.Ready);
        Assert.Equal(GeotagSource.None, rejected.GeotagSource);
        Assert.Equal((FalsterboLat, FalsterboLon, GpsRejectionReason.Spike), (rejected.GpsRejection!.Latitude, rejected.GpsRejection.Longitude, rejected.GpsRejection.Reason));
        Assert.Equal(1, await FalsterboPinsAsync(api));
        var listed = await api.GetFromJsonAsync<PhotoListResponse>("/photos", Json);
        Assert.Equal(GpsRejectionReason.Spike, listed!.Items.Single(i => i.Id == spike).GpsRejection?.Reason);

        var named = new GpsSweepRequest { Apply = true, RejectCoordinates = [new GpsCoordinateDto { Latitude = repeat.Latitude, Longitude = repeat.Longitude }] };
        Assert.Equal([stale], (await SweepAsync(api, named)).RejectedIds);
        Assert.Equal(GeotagSource.None, (await WaitForStatusAsync(api, stale, AssetStatus.Ready)).GeotagSource);
        Assert.Equal(0, await FalsterboPinsAsync(api));

        (await api.PostAsync($"/photos/{stale}/reprocess", null)).EnsureSuccessStatusCode();
        Assert.Equal(GeotagSource.None, (await WaitForStatusAsync(api, stale, AssetStatus.Ready)).GeotagSource);

        (await api.DeleteAsync($"/photos/{stale}/gps-rejection")).EnsureSuccessStatusCode();
        var restored = await WaitForStatusAsync(api, stale, AssetStatus.Ready);
        Assert.Equal((GeotagSource.ExifGps, FalsterboLat), (restored.GeotagSource, restored.Latitude!.Value));
        Assert.Null(restored.GpsRejection);
    }
}
