using LupiraPhotoApi.Core.Domain;
using Marten;

namespace LupiraPhotoApi.Core.Application.Import;

/// <summary>
/// Finds the copies byte-level dedup misses: Google Photos re-compresses what it stores, and rewrites video
/// metadata, so a Takeout file rarely matches its original's size or hash. A copy keeps its name and capture
/// time, though. Same name plus the same capture second is a copy; so is the same name within a day at a
/// size within 1%. Camera counters (<c>DSC_0122</c>) repeat across years, so a name alone never is.
/// </summary>
public sealed class NearCopyIndex(IEnumerable<NearCopy> stored)
{
    private static readonly TimeSpan SameSecond = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SameDay = TimeSpan.FromDays(1);

    private readonly ILookup<(AssetKind, string), NearCopy> _byName = stored.ToLookup(c => (c.Kind, c.Name));

    public static async Task<NearCopyIndex> LoadAsync(IQuerySession session, Guid principalId, CancellationToken ct)
    {
        var assets = await session.Query<PhotoAsset>()
            .Where(a => a.PrincipalId == principalId && a.DeviceId.StartsWith("import:") && a.Status != AssetStatus.Declared)
            .Select(a => new
            {
                a.Id,
                a.DuplicateOfId,
                a.Kind,
                a.MediaStoreId,
                a.TakenAt,
                a.SizeBytes,
                a.SourceAlbum,
                a.SourceAlbumKind,
                a.SourceAlbumDate,
            })
            .ToListAsync(ct);

        return new NearCopyIndex(assets.Select(a => new NearCopy
        {
            CanonicalId = a.DuplicateOfId ?? a.Id,
            Kind = a.Kind,
            Name = CopyName.Normalize(a.MediaStoreId),
            TakenAt = a.TakenAt,
            SizeBytes = a.SizeBytes,
            SourceAlbum = a.SourceAlbum,
            SourceAlbumKind = a.SourceAlbumKind,
            SourceAlbumDate = a.SourceAlbumDate,
        }));
    }

    public NearCopy? Find(AssetKind kind, string path, DateTimeOffset takenAt, long sizeBytes) =>
        _byName[(kind, CopyName.Normalize(path))].FirstOrDefault(c =>
        {
            var apart = (c.TakenAt - takenAt).Duration();
            return apart < SameSecond
                || (apart <= SameDay && Math.Abs(c.SizeBytes - sizeBytes) <= Math.Max(c.SizeBytes, sizeBytes) / 100);
        });
}
