using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using Marten;

namespace LupiraPhotoApi.Core.Application;

/// <summary>Imported albums summarised for the event-linking review. Aggregated in memory over a thin
/// projection — a family library has hundreds of albums, not millions.</summary>
public sealed class PhotoAlbumService(IQuerySession session)
{
    private const string ImportDevicePrefix = "import:";

    public async Task<List<PhotoAlbumDto>> ListAsync(Guid principalId, CancellationToken ct)
    {
        var rows = await session.Query<PhotoAsset>()
            .Where(a => a.PrincipalId == principalId && a.SourceAlbum != null && a.Status != AssetStatus.Duplicate && a.TrashedAt == null)
            .Select(a => new AlbumRow
            {
                Album = a.SourceAlbum!,
                Kind = a.SourceAlbumKind,
                Date = a.SourceAlbumDate,
                DeviceId = a.DeviceId,
                TakenAt = a.TakenAt,
                TakenAtSource = a.TakenAtSource,
                Latitude = a.Latitude,
                Longitude = a.Longitude,
            })
            .ToListAsync(ct);

        return rows.GroupBy(r => r.Album)
            .Select(Summarise)
            .OrderBy(a => a.CoreFrom ?? a.FolderDate ?? DateOnly.MaxValue)
            .ThenBy(a => a.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static PhotoAlbumDto Summarise(IGrouping<string, AlbumRow> album)
    {
        var exactDays = album.Where(r => !CaptureTime.IsApproximate(r.TakenAtSource))
            .Select(r => DateOnly.FromDateTime(r.TakenAt.UtcDateTime))
            .ToList();
        var core = CoreSpan.Compute(exactDays);
        var located = album.Where(r => r.Latitude is not null && r.Longitude is not null).ToList();
        var first = album.First();

        return new PhotoAlbumDto
        {
            Name = album.Key,
            Kind = first.Kind ?? AlbumKind.Event,
            Source = first.DeviceId.StartsWith(ImportDevicePrefix, StringComparison.Ordinal) ? first.DeviceId[ImportDevicePrefix.Length..] : null,
            FolderDate = album.Select(r => r.Date).FirstOrDefault(d => d is not null),
            Count = album.Count(),
            CoreFrom = core?.From,
            CoreTo = core?.To,
            Outliers = album.Count() - (core?.Count ?? 0),
            CentroidLatitude = Median(located.Select(r => r.Latitude!.Value)),
            CentroidLongitude = Median(located.Select(r => r.Longitude!.Value)),
        };
    }

    private static double? Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0) return null;
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private sealed class AlbumRow
    {
        public required string Album { get; set; }

        public AlbumKind? Kind { get; set; }

        public DateOnly? Date { get; set; }

        public required string DeviceId { get; set; }

        public DateTimeOffset TakenAt { get; set; }

        public TakenAtSource TakenAtSource { get; set; }

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }
    }
}
