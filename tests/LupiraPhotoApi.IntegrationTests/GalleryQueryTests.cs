using System.Net;
using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraPhotoApi.Core.Application;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Xunit;

namespace LupiraPhotoApi.IntegrationTests;

/// <summary>The gallery's read surface: sort direction, the filters it exposes, batch lookup, stats.</summary>
public class GalleryQueryTests(PhotoApiTestFactory factory) : IntegrationTest(factory)
{
    /// <summary>Five assets on consecutive days, the middle one ungeotagged.</summary>
    private async Task<List<Guid>> SeedAsync(HttpClient api)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var bytes = TinyJpeg(i);
            var request = PhotoDeclare(bytes, mediaStoreId: $"media-{i}", lat: i == 2 ? null : 59.33, lon: i == 2 ? null : 18.07);
            request.TakenAt = new DateTimeOffset(2026, 8, 1 + i, 12, 0, 0, TimeSpan.Zero);
            ids.Add(await UploadFlowAsync(api, request, bytes));
        }
        return ids;
    }

    private static async Task<List<PhotoListItemDto>> DrainAsync(HttpClient api, string query)
    {
        var all = new List<PhotoListItemDto>();
        string? cursor = null;
        do
        {
            var url = cursor is null ? query : $"{query}&cursor={Uri.EscapeDataString(cursor)}";
            var page = await api.GetFromJsonAsync<PhotoListResponse>(url, Json);
            all.AddRange(page!.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return all;
    }

    [Fact]
    public async Task AscendingAndDescendingCoverTheSameSetExactlyOnce()
    {
        var api = Factory.ApiClient("anna@example.com");
        var seeded = await SeedAsync(api);

        var desc = await DrainAsync(api, "/photos?limit=2&sort=TakenAtDesc");
        var asc = await DrainAsync(api, "/photos?limit=2&sort=TakenAtAsc");

        // The whole point of the keyset flip: neither direction may skip or repeat a row.
        Assert.Equal(seeded.Count, desc.Count);
        Assert.Equal(seeded.Count, asc.Count);
        Assert.Equal(seeded.OrderBy(i => i), desc.Select(i => i.Id).OrderBy(i => i));
        Assert.Equal(desc.Select(i => i.Id).Reverse(), asc.Select(i => i.Id));
        Assert.Equal(asc.Select(i => i.TakenAt).OrderBy(t => t), asc.Select(i => i.TakenAt));
    }

    [Fact]
    public async Task ACursorFromTheOtherSortIsRejected()
    {
        var api = Factory.ApiClient("anna@example.com");
        await SeedAsync(api);

        var page = await api.GetFromJsonAsync<PhotoListResponse>("/photos?limit=2&sort=TakenAtDesc", Json);
        var response = await api.GetAsync($"/photos?limit=2&sort=TakenAtAsc&cursor={Uri.EscapeDataString(page!.NextCursor!)}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LocatedFiltersOnWhetherTheAssetHasCoordinates()
    {
        var api = Factory.ApiClient("anna@example.com");
        await SeedAsync(api);

        var located = await DrainAsync(api, "/photos?limit=50&located=true");
        var unlocated = await DrainAsync(api, "/photos?limit=50&located=false");

        Assert.Equal(4, located.Count);
        Assert.All(located, i => Assert.NotNull(i.Latitude));
        // The ungeotagged one is invisible on the map — this is the only place it shows up.
        Assert.Single(unlocated);
        Assert.All(unlocated, i => Assert.Null(i.Latitude));
    }

    [Fact]
    public async Task PlaceMatchesCaseInsensitiveSubstring()
    {
        var api = Factory.ApiClient("anna@example.com");
        Factory.Geo.Label = "Stockholm";
        await SeedAsync(api);
        foreach (var id in await DrainAsync(api, "/photos?limit=50&located=true"))
            await WaitForStatusAsync(api, id.Id, AssetStatus.Ready);

        Assert.NotEmpty(await DrainAsync(api, "/photos?limit=50&place=sto"));
        Assert.NotEmpty(await DrainAsync(api, "/photos?limit=50&place=HOLM"));
        Assert.Empty(await DrainAsync(api, "/photos?limit=50&place=paris"));
    }

    [Fact]
    public async Task LookupHydratesByIdAndStaysOwnerScoped()
    {
        var anna = Factory.ApiClient("anna@example.com");
        var erik = Factory.ApiClient("erik@example.com");
        var seeded = await SeedAsync(anna);

        var mine = await anna.PostAsJsonAsync("/photos/lookup", new LookupPhotosRequest { Ids = [.. seeded.Take(2)] }, Json);
        var got = (await mine.Content.ReadFromJsonAsync<PhotoListResponse>(Json))!;
        Assert.Equal(2, got.Items.Count);

        // An unknown id is simply absent rather than an error…
        var withGhost = await anna.PostAsJsonAsync("/photos/lookup", new LookupPhotosRequest { Ids = [seeded[0], Guid.NewGuid()] }, Json);
        Assert.Single((await withGhost.Content.ReadFromJsonAsync<PhotoListResponse>(Json))!.Items);

        // …and another principal's ids are equally absent, never someone else's photo.
        var theirs = await erik.PostAsJsonAsync("/photos/lookup", new LookupPhotosRequest { Ids = seeded }, Json);
        Assert.Empty((await theirs.Content.ReadFromJsonAsync<PhotoListResponse>(Json))!.Items);
    }

    [Fact]
    public async Task LookupRejectsAnOversizedBatch()
    {
        var api = Factory.ApiClient("anna@example.com");
        var ids = Enumerable.Range(0, PhotoQueryService.LookupMax + 1).Select(_ => Guid.NewGuid()).ToList();
        var response = await api.PostAsJsonAsync("/photos/lookup", new LookupPhotosRequest { Ids = ids }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListItemsCarryTheMetadataTheGalleryShows()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();
        var id = await UploadFlowAsync(api, PhotoDeclare(bytes), bytes);
        await WaitForStatusAsync(api, id, AssetStatus.Ready);

        var item = (await api.GetFromJsonAsync<PhotoListResponse>("/photos", Json))!.Items.Single();
        // Without these on the list DTO every tile would need its own detail request.
        Assert.Equal("image/jpeg", item.ContentType);
        Assert.Equal(bytes.Length, item.SizeBytes);
        Assert.Equal(GeotagSource.ExifGps, item.GeotagSource);
        Assert.NotNull(item.ThumbUrl);
    }

    [Fact]
    public async Task StatsReportTheLibrary()
    {
        var api = Factory.ApiClient("anna@example.com");
        var seeded = await SeedAsync(api);

        var stats = await api.GetFromJsonAsync<PhotoStats>("/photos/stats", Json);
        Assert.Equal(seeded.Count, stats!.TotalAssets);
        Assert.True(stats.TotalBytes > 0);
        Assert.Equal(seeded.Count, stats.ByKind["Photo"]);
        // ByMonth is what the gallery's date scrubber indexes on.
        Assert.Equal(seeded.Count, stats.ByMonth["2026-08"]);
    }

    [Fact]
    public async Task PlacesSuggestLabelsByUseAndMatchLikeThePlaceFilter()
    {
        var anna = Factory.ApiClient("anna@example.com");
        var me = await PrincipalIdAsync(anna);
        var erik = await PrincipalIdAsync(Factory.ApiClient("erik@example.com"));
        PhotoAsset Placed(Guid owner, string label)
        {
            var asset = Seeded(owner);
            asset.PlaceLabel = label;
            return asset;
        }

        var trashed = Placed(me, "Paris");
        trashed.TrashedAt = DateTimeOffset.UtcNow;
        var duplicate = Placed(me, "Paris");
        duplicate.Status = AssetStatus.Duplicate;
        await StoreAsync(
            Placed(me, "Stockholm"), Placed(me, "Stockholm"), Placed(me, "Stockholm"),
            Placed(me, "Uppsala"), Placed(me, "Uppsala"), Placed(me, "Gothenburg"),
            trashed, duplicate, Placed(erik, "Berlin"), Seeded(me));

        var all = await anna.GetFromJsonAsync<List<PhotoPlaceCount>>("/photos/places", Json);
        Assert.Equal([("Stockholm", 3), ("Uppsala", 2), ("Gothenburg", 1)], all!.Select(p => (p.Label, p.Count)));

        Assert.Equal(["Stockholm"], (await anna.GetFromJsonAsync<List<PhotoPlaceCount>>("/photos/places?q=HOLM", Json))!.Select(p => p.Label));
        Assert.Equal(["Stockholm"], (await anna.GetFromJsonAsync<List<PhotoPlaceCount>>("/photos/places?limit=1", Json))!.Select(p => p.Label));

        // A suggestion's count is what picking it in the place filter returns.
        var suggested = await anna.GetFromJsonAsync<List<PhotoPlaceCount>>("/photos/places?q=o", Json);
        Assert.Equal((await DrainAsync(anna, "/photos?limit=50&place=o")).Count, suggested!.Sum(p => p.Count));
    }

    [Fact]
    public async Task RepeatedListingsHandOutTheSameThumbnailUrl()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();
        var id = await UploadFlowAsync(api, PhotoDeclare(bytes), bytes);
        await WaitForStatusAsync(api, id, AssetStatus.Ready);

        var first = (await api.GetFromJsonAsync<PhotoListResponse>("/photos", Json))!.Items.Single().ThumbUrl;
        // SigV4 stamps the signing second into the URL, so a fresh signature would differ by now.
        await Task.Delay(TimeSpan.FromSeconds(1.1));
        var second = (await api.GetFromJsonAsync<PhotoListResponse>("/photos", Json))!.Items.Single().ThumbUrl;

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task StatsAreOwnerScoped()
    {
        await SeedAsync(Factory.ApiClient("anna@example.com"));
        var stats = await Factory.ApiClient("erik@example.com").GetFromJsonAsync<PhotoStats>("/photos/stats", Json);
        Assert.Equal(0, stats!.TotalAssets);
    }

    [Fact]
    public async Task AWindowWithANonUtcOffsetFiltersOnTheSameInstant()
    {
        var owner = await PrincipalIdAsync(Factory.ApiClient("anna@example.com"));
        var inside = Located(owner, 59.33, 18.07, new DateTimeOffset(2015, 8, 9, 9, 0, 0, TimeSpan.Zero));
        var before = Located(owner, 59.33, 18.07, new DateTimeOffset(2015, 8, 9, 6, 0, 0, TimeSpan.Zero));
        await StoreAsync(inside, before);

        // 10:00+02:00 is 08:00Z, so only the 09:00Z asset is inside.
        var from = Uri.EscapeDataString("2015-08-09T10:00:00+02:00");
        var to = Uri.EscapeDataString("2015-08-09T12:00:00+02:00");
        var items = (await Factory.ApiClient("anna@example.com").GetFromJsonAsync<PhotoListResponse>($"/photos?from={from}&to={to}", Json))!.Items;

        Assert.Equal([inside.Id], items.Select(i => i.Id));
    }
}
