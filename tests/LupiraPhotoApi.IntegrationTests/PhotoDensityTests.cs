using System.Net;
using System.Net.Http.Json;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Xunit;

namespace LupiraPhotoApi.IntegrationTests;

/// <summary>Where the owner's photos were measured to be taken, per ~100 m cell.</summary>
public class PhotoDensityTests(PhotoApiTestFactory factory) : IntegrationTest(factory)
{
    private static PhotoAsset Located(Guid owner, double lat, double lon, DateTimeOffset takenAt, GeotagSource source = GeotagSource.ExifGps)
    {
        var asset = Seeded(owner);
        asset.Latitude = lat;
        asset.Longitude = lon;
        asset.GeotagSource = source;
        asset.TakenAt = takenAt;
        return asset;
    }

    private static async Task<List<PhotoDensityCellDto>> DensityAsync(HttpClient api, string query = "") =>
        (await api.GetFromJsonAsync<List<PhotoDensityCellDto>>($"/photos/density{query}", Json))!;

    [Fact]
    public async Task CountsOnlyTheOwnersReadyUntrashedMeasuredLocations()
    {
        var anna = Factory.ApiClient("anna@example.com");
        var erik = Factory.ApiClient("erik@example.com");
        var me = await PrincipalIdAsync(anna);
        var other = await PrincipalIdAsync(erik);
        var day = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

        var trashed = Located(me, 59.33, 18.07, day);
        trashed.TrashedAt = DateTimeOffset.UtcNow;
        var failed = Located(me, 59.33, 18.07, day);
        failed.Status = AssetStatus.Failed;
        var duplicate = Located(me, 59.33, 18.07, day);
        duplicate.Status = AssetStatus.Duplicate;
        await StoreAsync(
            Located(me, 59.33, 18.07, day),
            Located(me, 59.3301, 18.0701, day.AddDays(1), GeotagSource.LocationHistory),
            Located(me, 57.70, 11.97, day, GeotagSource.Folder),
            trashed, failed, duplicate, Seeded(me),
            Located(other, 55.60, 13.00, day));

        var cell = Assert.Single(await DensityAsync(anna));
        Assert.Equal((59.33, 18.07, 2), (cell.Latitude, cell.Longitude, cell.Count));
        Assert.Equal([new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 2)], cell.Days);

        Assert.Equal([(55.6, 13.0)], (await DensityAsync(erik)).Select(c => (c.Latitude, c.Longitude)));
    }

    [Fact]
    public async Task FromAndToAreInclusive()
    {
        var anna = Factory.ApiClient("anna@example.com");
        var me = await PrincipalIdAsync(anna);
        var days = Enumerable.Range(1, 5).Select(d => new DateTimeOffset(2026, 8, d, 12, 0, 0, TimeSpan.Zero)).ToList();
        await StoreAsync([.. days.Select(d => Located(me, 59.33, 18.07, d))]);

        var cell = Assert.Single(await DensityAsync(anna, "?from=2026-08-02T12:00:00Z&to=2026-08-04T12:00:00Z"));
        Assert.Equal(3, cell.Count);
        Assert.Equal([new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 4)], cell.Days);

        Assert.Equal(2, Assert.Single(await DensityAsync(anna, "?from=2026-08-04T12:00:00Z")).Count);
        Assert.Equal(1, Assert.Single(await DensityAsync(anna, "?to=2026-08-01T12:00:00Z")).Count);
    }

    [Fact]
    public async Task FromAfterToIsRejected()
    {
        var response = await Factory.ApiClient("anna@example.com")
            .GetAsync("/photos/density?from=2026-08-02T00:00:00Z&to=2026-08-01T00:00:00Z");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
