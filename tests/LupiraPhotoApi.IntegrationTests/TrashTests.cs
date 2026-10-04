using System.Net;
using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraPhotoApi.Core.Application;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupiraPhotoApi.IntegrationTests;

/// <summary>Soft delete: the trash hides without losing anything, restore is lossless, and only a purge
/// removes bytes.</summary>
public class TrashTests(PhotoApiTestFactory factory) : IntegrationTest(factory)
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(new PhotoOptions().TrashRetentionDays);

    private static async Task<List<Guid>> SeedAsync(HttpClient api, int count)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var bytes = TinyJpeg(i);
            var request = PhotoDeclare(bytes, mediaStoreId: $"media-{i}");
            request.TakenAt = new DateTimeOffset(2026, 8, 1 + i, 12, 0, 0, TimeSpan.Zero);
            ids.Add(await UploadFlowAsync(api, request, bytes));
        }
        return ids;
    }

    private static async Task<PhotoAssetDto> PostAsync(HttpClient api, string path)
    {
        var response = await api.PostAsync(path, null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PhotoAssetDto>(Json))!;
    }

    private static async Task<List<Guid>> ListedAsync(HttpClient api, string query = "/photos?limit=50") =>
        [.. (await api.GetFromJsonAsync<PhotoListResponse>(query, Json))!.Items.Select(i => i.Id)];

    private async Task<PhotoAsset?> LoadAsync(Guid id)
    {
        await using var session = Factory.Store.QuerySession();
        return await session.LoadAsync<PhotoAsset>(id);
    }

    [Fact]
    public async Task TrashingHidesTheAssetButKeepsItsStatusAndBytes()
    {
        var api = Factory.ApiClient("anna@example.com");
        var ids = await SeedAsync(api, 2);
        foreach (var id in ids) await WaitForStatusAsync(api, id, AssetStatus.Ready);
        var objects = Factory.S3.Objects.Count;

        var trashed = await PostAsync(api, $"/photos/{ids[0]}/trash");

        Assert.Equal(AssetStatus.Ready, trashed.Status);
        Assert.NotNull(trashed.TrashedAt);
        Assert.Equal(trashed.TrashedAt + Retention, trashed.PurgesAt);
        Assert.Equal([ids[1]], await ListedAsync(api));
        Assert.Equal(objects, Factory.S3.Objects.Count);

        var bin = (await api.GetFromJsonAsync<PhotoListResponse>("/photos?trashed=true", Json))!.Items;
        Assert.Equal([ids[0]], bin.Select(i => i.Id));
        Assert.Equal(trashed.PurgesAt, bin[0].PurgesAt);

        // Still addressable directly, so the trash view can open it.
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync($"/photos/{ids[0]}")).StatusCode);
    }

    [Fact]
    public async Task TrashingTwiceKeepsTheOriginalTimestamp()
    {
        var api = Factory.ApiClient("anna@example.com");
        var id = (await SeedAsync(api, 1))[0];

        var first = await PostAsync(api, $"/photos/{id}/trash");
        var second = await PostAsync(api, $"/photos/{id}/trash");

        Assert.Equal(first.TrashedAt, second.TrashedAt);
    }

    [Fact]
    public async Task RestoreReturnsTheAssetToTheLibrary()
    {
        var api = Factory.ApiClient("anna@example.com");
        var id = (await SeedAsync(api, 1))[0];
        await PostAsync(api, $"/photos/{id}/trash");

        var restored = await PostAsync(api, $"/photos/{id}/restore");
        var again = await PostAsync(api, $"/photos/{id}/restore");

        Assert.Null(restored.TrashedAt);
        Assert.Null(restored.PurgesAt);
        Assert.Null(again.TrashedAt);
        Assert.Equal([id], await ListedAsync(api));
        Assert.Empty(await ListedAsync(api, "/photos?trashed=true"));
    }

    [Fact]
    public async Task TrashAndRestoreAreOwnerScoped()
    {
        var id = (await SeedAsync(Factory.ApiClient("anna@example.com"), 1))[0];
        var erik = Factory.ApiClient("erik@example.com");

        Assert.Equal(HttpStatusCode.NotFound, (await erik.PostAsync($"/photos/{id}/trash", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await erik.PostAsync($"/photos/{id}/restore", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await erik.PostAsync($"/photos/{Guid.NewGuid()}/trash", null)).StatusCode);
        Assert.Null((await LoadAsync(id))!.TrashedAt);
    }

    [Fact]
    public async Task TheTrashPagesInBothSortOrders()
    {
        var api = Factory.ApiClient("anna@example.com");
        var ids = await SeedAsync(api, 5);
        foreach (var id in ids.Skip(1)) await PostAsync(api, $"/photos/{id}/trash");

        var desc = new List<Guid>();
        var asc = new List<Guid>();
        foreach (var (sort, into) in new[] { ("TakenAtDesc", desc), ("TakenAtAsc", asc) })
        {
            string? cursor = null;
            do
            {
                var url = $"/photos?trashed=true&limit=3&sort={sort}" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
                var page = (await api.GetFromJsonAsync<PhotoListResponse>(url, Json))!;
                into.AddRange(page.Items.Select(i => i.Id));
                cursor = page.NextCursor;
            }
            while (cursor is not null);
        }

        Assert.Equal(ids.Skip(1).Reverse(), desc);
        Assert.Equal(ids.Skip(1), asc);
    }

    [Fact]
    public async Task TrashedAssetsAreLeftOutOfLookupMapAndStats()
    {
        var api = Factory.ApiClient("anna@example.com");
        var ids = await SeedAsync(api, 2);
        foreach (var id in ids) await WaitForStatusAsync(api, id, AssetStatus.Ready);
        await PostAsync(api, $"/photos/{ids[0]}/trash");

        var lookup = await api.PostAsJsonAsync("/photos/lookup", new LookupPhotosRequest { Ids = ids }, Json);
        Assert.Equal([ids[1]], (await lookup.Content.ReadFromJsonAsync<PhotoListResponse>(Json))!.Items.Select(i => i.Id));

        var map = await api.GetFromJsonAsync<PhotoMapResponse>("/photos/map?bbox=17,58,19,60", Json);
        Assert.Equal([ids[1]], map!.Features.Select(f => f.Properties.Id));

        var stats = (await api.GetFromJsonAsync<PhotoStats>("/photos/stats", Json))!;
        Assert.Equal((1, 1), (stats.TotalAssets, stats.TrashedAssets));
        Assert.Equal(1, stats.ByMonth["2026-08"]);
    }

    [Fact]
    public async Task AlbumsLeaveOutTrashedAssets()
    {
        var api = Factory.ApiClient("anna@example.com");
        var me = await PrincipalIdAsync(api);
        var kept = Seeded(me);
        var binned = Seeded(me);
        var gone = Seeded(me);
        (kept.SourceAlbum, binned.SourceAlbum, gone.SourceAlbum) = ("2017-10-03 Svalbard", "2017-10-03 Svalbard", "2019-04-01 Paris");
        binned.TrashedAt = gone.TrashedAt = DateTimeOffset.UtcNow;
        await StoreAsync(kept, binned, gone);

        var albums = (await api.GetFromJsonAsync<List<PhotoAlbumDto>>("/photos/albums", Json))!;

        var album = Assert.Single(albums);
        Assert.Equal(("2017-10-03 Svalbard", 1), (album.Name, album.Count));
    }

    [Fact]
    public async Task EmptyingTheTrashPurgesOnlyTheCallersTrashedAssets()
    {
        var anna = Factory.ApiClient("anna@example.com");
        var erik = Factory.ApiClient("erik@example.com");
        var ids = await SeedAsync(anna, 2);
        var theirs = (await SeedAsync(erik, 1))[0];
        foreach (var id in ids) await WaitForStatusAsync(anna, id, AssetStatus.Ready);
        await PostAsync(anna, $"/photos/{ids[0]}/trash");
        await PostAsync(erik, $"/photos/{theirs}/trash");
        var binned = (await LoadAsync(ids[0]))!;

        var response = await anna.DeleteAsync("/photos/trash");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anna.GetAsync($"/photos/{ids[0]}")).StatusCode);
        Assert.False(Factory.S3.HasKey(binned.OriginalKey));
        Assert.False(Factory.S3.HasKey(binned.ThumbKey!));
        Assert.Equal([ids[1]], await ListedAsync(anna));
        Assert.NotNull(await LoadAsync(theirs));
    }

    [Fact]
    public async Task PermanentDeleteSkipsTheTrash()
    {
        var api = Factory.ApiClient("anna@example.com");
        var id = (await SeedAsync(api, 1))[0];
        await WaitForStatusAsync(api, id, AssetStatus.Ready);
        await PostAsync(api, $"/photos/{id}/trash");

        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/photos/{id}")).StatusCode);
        Assert.Null(await LoadAsync(id));
        Assert.Empty(Factory.S3.Objects);
    }

    [Fact]
    public async Task TheRetentionSweepPurgesOnlyWhatOutlivedIt()
    {
        var api = Factory.ApiClient("anna@example.com");
        var ids = await SeedAsync(api, 2);
        foreach (var id in ids) await WaitForStatusAsync(api, id, AssetStatus.Ready);
        var now = DateTimeOffset.UtcNow;
        var expired = (await LoadAsync(ids[0]))!;
        var recent = (await LoadAsync(ids[1]))!;
        expired.TrashedAt = now - Retention - TimeSpan.FromHours(1);
        recent.TrashedAt = now - Retention + TimeSpan.FromHours(1);
        await StoreAsync(expired, recent);

        using (var scope = Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<PhotoDeleteService>().PurgeTrashedBeforeAsync(now - Retention, default);

        Assert.Null(await LoadAsync(expired.Id));
        Assert.False(Factory.S3.HasKey(expired.OriginalKey));
        Assert.NotNull(await LoadAsync(recent.Id));
        Assert.True(Factory.S3.HasKey(recent.OriginalKey));
    }

    /// <summary>A second phone (or a re-import) must not resurrect a photo the user binned.</summary>
    [Fact]
    public async Task TheSamePhotoDeclaredAgainAfterTrashingIsAHiddenDuplicate()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();
        var canonical = await UploadFlowAsync(api, PhotoDeclare(bytes), bytes);
        await WaitForStatusAsync(api, canonical, AssetStatus.Ready);
        await PostAsync(api, $"/photos/{canonical}/trash");

        var second = PhotoDeclare(bytes, mediaStoreId: "media-2");
        second.DeviceId = "device-2";
        var declared = await DeclareAsync(api, second);

        Assert.Equal(AssetStatus.Duplicate, declared.Status);
        Assert.Null(declared.UploadUrl);
        Assert.Empty(await ListedAsync(api));

        await api.DeleteAsync("/photos/trash");
        Assert.Null(await LoadAsync(declared.AssetId));
    }

    [Fact]
    public async Task IdenticalBytesUploadedAfterTrashingCollapseOntoTheTrashedCanonical()
    {
        var api = Factory.ApiClient("anna@example.com");
        var bytes = TinyJpeg();
        var canonical = await UploadFlowAsync(api, PhotoDeclare(bytes), bytes);
        await WaitForStatusAsync(api, canonical, AssetStatus.Ready);
        await PostAsync(api, $"/photos/{canonical}/trash");

        var second = PhotoDeclare(bytes, mediaStoreId: "media-2");
        second.DeviceId = "device-2";
        second.TakenAt = second.TakenAt.AddSeconds(1);
        var copy = await UploadFlowAsync(api, second, bytes);

        var dto = await WaitForStatusAsync(api, copy, AssetStatus.Duplicate);
        Assert.Equal(canonical, dto.DuplicateOfId);
        Assert.Empty(await ListedAsync(api));
    }
}
