using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraPhotoApi.Core.Application;
using LupiraPhotoApi.Core.Application.Map;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Marten;
using Xunit;

namespace LupiraPhotoApi.IntegrationTests;

/// <summary>The map layer: every area with photos shows up at every zoom, however many newer photos crowd the view.</summary>
public class PhotoMapTests(PhotoApiTestFactory factory) : IntegrationTest(factory)
{
    private const double SpetebyhallLat = 58.9964;
    private const double SpetebyhallLon = 16.4106;
    private const string Sweden = "10,55,25,69";
    private const string Valla = "16.35,58.97,16.47,59.02";

    private static Task<PhotoMapResponse?> MapAsync(HttpClient api, string query) =>
        api.GetFromJsonAsync<PhotoMapResponse>($"/photos/map?{query}", Json);

    /// <summary>More recent Stockholm photos than the old per-request cap, plus Spetebyhall's 31 folder-placed ones from 2010–2017.</summary>
    private async Task<List<Guid>> SeedPackedLibraryAsync(Guid owner)
    {
        var recent = Enumerable.Range(0, PhotoQueryService.MapLimit + 100).Select(i => Located(
            owner,
            59.33 + ((i % 40) * 0.0002),
            18.07 + ((i / 40) * 0.0002),
            new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero).AddHours(i)));
        var old = Enumerable.Range(0, 31).Select(i => Located(
            owner,
            SpetebyhallLat,
            SpetebyhallLon,
            new DateTimeOffset(2010 + (i % 8), 6, 1, 12, 0, 0, TimeSpan.Zero).AddDays(i),
            GeotagSource.Folder)).ToList();

        await Factory.Store.BulkInsertAsync(recent.Concat(old).ToList());
        return [.. old.Select(a => a.Id)];
    }

    [Fact]
    public async Task AWideViewKeepsAnOldPlaceCrowdedOutByNewerPhotos()
    {
        var api = Factory.ApiClient("anna@example.com");
        await SeedPackedLibraryAsync(await PrincipalIdAsync(api));

        var map = (await MapAsync(api, $"bbox={Sweden}&zoom=5"))!;

        var spetebyhall = Assert.Single(map.Features, f => f.Properties.Count == 31);
        Assert.Equal(SpetebyhallLon, spetebyhall.Geometry.Coordinates[0], 9);
        Assert.Equal(SpetebyhallLat, spetebyhall.Geometry.Coordinates[1], 9);
        Assert.Equal([SpetebyhallLon, SpetebyhallLat, SpetebyhallLon, SpetebyhallLat], spetebyhall.Properties.Bounds!);
        Assert.Null(spetebyhall.Properties.Id);
        Assert.Equal(PhotoQueryService.MapLimit + 131, map.Features.Sum(f => f.Properties.Count));
    }

    [Fact]
    public async Task ZoomedInAtVallaEveryPhotoIsItsOwnPin()
    {
        var api = Factory.ApiClient("anna@example.com");
        var old = await SeedPackedLibraryAsync(await PrincipalIdAsync(api));

        var map = (await MapAsync(api, $"bbox={Valla}&zoom=13"))!;

        Assert.All(map.Features, f => Assert.Equal(1, f.Properties.Count));
        Assert.Equal(old.Order(), map.Features.Select(f => f.Properties.Id!.Value).Order());
        Assert.All(map.Features, f => Assert.NotNull(f.Properties.TakenAt));
    }

    [Fact]
    public async Task FromAndToFilterTheCells()
    {
        var api = Factory.ApiClient("anna@example.com");
        await SeedPackedLibraryAsync(await PrincipalIdAsync(api));

        var recent = (await MapAsync(api, $"bbox={Sweden}&zoom=5&from=2020-01-01T00:00:00Z"))!;
        var old = (await MapAsync(api, $"bbox={Sweden}&zoom=5&to=2020-01-01T00:00:00Z"))!;

        Assert.DoesNotContain(recent.Features, f => f.Geometry.Coordinates[0] < 17);
        Assert.Equal(PhotoQueryService.MapLimit + 100, recent.Features.Sum(f => f.Properties.Count));
        Assert.Equal(31, old.Features.Count(f => f.Properties.Count == 1 && f.Geometry.Coordinates[0] < 17));
    }

    [Fact]
    public async Task StreetLevelShowsPinsEvenWhereTheyCrowdOneSpot()
    {
        var api = Factory.ApiClient("anna@example.com");
        var me = await PrincipalIdAsync(api);
        await Factory.Store.BulkInsertAsync(Enumerable.Range(0, PhotoQueryService.PinLimit + 50)
            .Select(i => Located(me, 59.33, 18.07, new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero).AddHours(i)))
            .ToList());
        const string street = "18.06,59.325,18.08,59.335";

        Assert.Equal(PhotoQueryService.PinLimit + 50, Assert.Single((await MapAsync(api, $"bbox={street}&zoom=16.9"))!.Features).Properties.Count);
        Assert.Equal(PhotoQueryService.PinLimit + 50, (await MapAsync(api, $"bbox={street}&zoom=17"))!.Features.Count);
    }

    [Fact]
    public async Task ZoomedOutOverTheWholeLibraryTheResponseStaysBounded()
    {
        var api = Factory.ApiClient("anna@example.com");
        var me = await PrincipalIdAsync(api);
        var random = new Random(24);
        var library = Enumerable.Range(0, 24_000).Select(i => Located(
            me,
            (random.NextDouble() * 160) - 80,
            (random.NextDouble() * 360) - 180,
            new DateTimeOffset(2006, 1, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i * 7))).ToList();
        await Factory.Store.BulkInsertAsync(library);

        var perSide = PhotoMapGrid.MaxCellsPerSide + 1;
        foreach (var query in new[] { "bbox=-180,-85,180,85", "bbox=-180,-85,180,85&zoom=12" })
        {
            var map = (await MapAsync(api, query))!;
            Assert.InRange(map.Features.Count, 1, perSide * perSide);
            Assert.Equal(24_000, map.Features.Sum(f => f.Properties.Count));
        }
    }
}
