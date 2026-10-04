using System.Text.RegularExpressions;
using LupiraPhotoApi.Core.Application.Processing;
using LupiraPhotoApi.Core.Domain;
using LupiraPhotoApi.Core.Dtos.Photos;
using LupiraPhotoApi.Core.Storage;
using Marten;
using Microsoft.Extensions.Logging;

namespace LupiraPhotoApi.Core.Application.Import;

/// <summary>
/// One-shot import of a folder tree. Every file goes through the same declare → put → complete path a phone
/// uses, under <c>deviceId = import:&lt;source&gt;</c> and <c>mediaStoreId = &lt;relative path&gt;</c>, so a re-run
/// is idempotent and refreshes metadata from a corrected map file. Dedup and metadata donation apply as usual.
/// </summary>
public sealed partial class PhotoImporter(
    IDocumentSession session,
    PhotoDeclareService declareService,
    PhotoCompleteService completeService,
    IObjectStore store,
    IMediaMetadataReader metadataReader,
    IPlaceResolver placeResolver,
    ILogger<PhotoImporter> logger)
{
    private static readonly HashSet<string> JunkExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".thm", ".zip", ".rar", ".7z", ".xlsx", ".docx", ".pdf", ".psd", ".db", ".ini", ".txt", ".rtf", ".url", ".lnk",
    };

    private static readonly HashSet<string> JunkNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Thumbs.db", "desktop.ini", ".DS_Store", "metadata.json", "print-subscriptions.json", "shared_album_comments.json",
        "user-generated-memory-titles.json",
    };

    public async Task<ImportReport> RunAsync(ImportRequest request, CancellationToken ct)
    {
        var report = new ImportReport { DryRun = request.DryRun };
        report.Errors.AddRange(request.Map.Errors);

        var titles = request.Source == ImportSource.Takeout ? TakeoutTitles.Load(request.Root) : [];
        var files = await ScanAsync(request, titles, report, ct);
        ResolveCaptureTimes(request, files, report);
        await ResolvePlacesAsync(request, files, report, ct);
        if (request.Source == ImportSource.Takeout)
        {
            var index = await NearCopyIndex.LoadAsync(session, request.PrincipalId, ct);
            foreach (var file in files)
                file.NearCopy = index.Find(file.Kind, file.Relative, file.Capture!.TakenAt, file.SizeBytes);
            await AdoptAlbumsAsync(request, files, report, ct);
        }

        ResolvePhotographers(request, files, report);
        SummariseAlbums(files, report);

        if (request.DryRun)
        {
            await CountExpectedDuplicatesAsync(request, files, report, ct);
            return report;
        }

        if (report.Errors.Count > 0)
        {
            report.Errors.Add("Nothing written — fix the errors above and re-run.");
            return report;
        }

        await WriteAsync(request, files, report, ct);
        return report;
    }

    private async Task<List<ImportFile>> ScanAsync(
        ImportRequest request, Dictionary<string, string> titles, ImportReport report, CancellationToken ct)
    {
        var files = new List<ImportFile>();
        var directories = Directory.EnumerateDirectories(request.Root, "*", SearchOption.AllDirectories).Prepend(request.Root);
        foreach (var dir in directories)
        {
            var names = Directory.EnumerateFiles(dir).Select(p => Path.GetFileName(p)).ToHashSet(StringComparer.Ordinal);
            foreach (var name in names.Order(StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                if (request.Source == ImportSource.Takeout && TakeoutSidecars.IsSidecar(name)) continue;
                var ext = Path.GetExtension(name);
                if (JunkNames.Contains(name) || JunkExtensions.Contains(ext) || EditedCopy().IsMatch(Path.GetFileNameWithoutExtension(name)))
                {
                    report.Skipped++;
                    continue;
                }

                if (!ObjectKeys.TryResolveExtension(ext, out var contentType) || !ObjectKeys.TryResolve(contentType, out _, out var kind))
                {
                    report.Count(report.Unsupported, ext.Length == 0 ? "(no extension)" : ext.ToLowerInvariant());
                    continue;
                }

                var fullPath = Path.Combine(dir, name);
                var relative = Path.GetRelativePath(request.Root, fullPath).Replace('\\', '/');
                var file = new ImportFile
                {
                    FullPath = fullPath,
                    Relative = relative,
                    Segments = relative.Split('/')[..^1],
                    ContentType = contentType,
                    Kind = kind,
                    SizeBytes = new FileInfo(fullPath).Length,
                    FileTimeUtc = File.GetLastWriteTimeUtc(fullPath),
                    Metadata = await metadataReader.ReadAsync(fullPath, kind, ct),
                };
                if (FilenameDate.TryParse(name, out var local, out var transfer))
                    (file.FilenameLocal, file.FilenameIsTransfer) = (local, transfer);
                if (request.Source == ImportSource.Takeout && TakeoutSidecars.Find(name, names) is { } sidecar)
                    file.Sidecar = TakeoutSidecars.Read(await File.ReadAllTextAsync(Path.Combine(dir, sidecar), ct));

                AssignAlbum(request, file, titles);
                files.Add(file);
                report.Count(report.ByKind, kind.ToString());
                if (files.Count % 500 == 0) logger.LogInformation("Scanned {Count} files…", files.Count);
            }
        }

        report.Files = files.Count;
        return files;
    }

    private static void AssignAlbum(ImportRequest request, ImportFile file, Dictionary<string, string> titles)
    {
        var segments = file.Segments;
        if (segments.Length == 0 || request.Source == ImportSource.Platser) return;

        if (request.Source == ImportSource.Takeout)
        {
            if (YearFolder().IsMatch(segments[0])) return;
            (file.Album, file.AlbumKind, file.AlbumDepth) = (titles.GetValueOrDefault(segments[0]) ?? segments[0], AlbumKind.Event, 0);
        }
        else
        {
            // The outermost dated folder is the event; person and day subfolders sit beneath it. With no dated
            // folder at all, the top folder is a theme (cats, a summer, old scans).
            file.AlbumDepth = 0;
            (file.Album, file.AlbumKind) = (segments[0], AlbumKind.Theme);
            for (var i = 0; i < segments.Length; i++)
            {
                var info = FolderName.Parse(segments[i]);
                if (info.Precision is FolderDatePrecision.Day or FolderDatePrecision.Month)
                {
                    (file.Album, file.AlbumKind, file.AlbumDepth) = (segments[i], AlbumKind.Event, i);
                    (file.AlbumDate, file.AlbumPrecision) = (info.Date, info.Precision);
                    break;
                }
            }
        }

        if (request.Map.Albums.TryGetValue(ImportMapParser.Collapse(file.Album), out var rule))
        {
            switch (rule.Kind)
            {
                case AlbumRuleKind.None:
                    (file.Album, file.AlbumDate) = (null, null);
                    break;
                case AlbumRuleKind.Theme:
                    file.AlbumKind = AlbumKind.Theme;
                    break;
                case AlbumRuleKind.Event:
                    file.AlbumKind = AlbumKind.Event;
                    break;
                case AlbumRuleKind.SameAs:
                    file.Album = rule.SameAs;
                    break;
            }
        }
    }

    private static void ResolveCaptureTimes(ImportRequest request, List<ImportFile> files, ImportReport report)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var file in files)
            file.Capture = CaptureTimeResolver.Resolve(Candidates(request, file, albumCoreStart: null), request.Zone, now, PrefersFilename(request, file));

        // Photos dated only approximately take their album's core start, which needs the exact-dated ones first.
        foreach (var album in files.Where(f => f.Album is not null).GroupBy(f => f.Album!))
        {
            var core = CoreSpan.Compute(album
                .Where(f => !CaptureTime.IsApproximate(f.Capture!.Source))
                .Select(f => DateOnly.FromDateTime(f.Capture!.TakenAt.UtcDateTime)));
            if (core is null) continue;
            foreach (var file in album.Where(f => CaptureTime.IsApproximate(f.Capture!.Source)))
                file.Capture = CaptureTimeResolver.Resolve(Candidates(request, file, core.Value.From), request.Zone, now, PrefersFilename(request, file));
        }

        foreach (var file in files)
        {
            var capture = file.Capture!;
            report.Count(report.CaptureSources, capture.Source);
            foreach (var rejected in capture.Rejected) report.RejectedDates.Add($"{file.Relative}: {rejected}");
            if (capture.Disagreement is { } d)
                report.Disagreements.Add($"{file.Relative}: exif {d.Exif:yyyy-MM-dd} vs name {d.Filename:yyyy-MM-dd}");
        }
    }

    private static CaptureCandidates Candidates(ImportRequest request, ImportFile file, DateOnly? albumCoreStart) => new()
    {
        ExifLocal = file.Metadata.TakenAtLocal,
        ExifOffset = file.Metadata.TakenAtOffset,
        CameraClock = CameraEntry(request.Map.Clocks, file.Metadata.Camera),
        VideoUtc = file.Metadata.TakenAtUtc,
        FilenameLocal = file.FilenameLocal,
        FilenameIsTransfer = file.FilenameIsTransfer,
        SidecarTakenUtc = file.Sidecar?.TakenUtc,
        SidecarUploadUtc = file.Sidecar?.UploadUtc,
        FolderDate = file.AlbumDate,
        FolderPrecision = file.AlbumPrecision,
        AlbumCoreStart = albumCoreStart,
        FileTimeUtc = new DateTimeOffset(file.FileTimeUtc, TimeSpan.Zero),
    };

    private static bool PrefersFilename(ImportRequest request, ImportFile file) =>
        (file.Album is not null && request.Map.PreferFilename.Contains(file.Album))
        || file.Segments.Any(s => request.Map.PreferFilename.Contains(s));

    private async Task ResolvePlacesAsync(ImportRequest request, List<ImportFile> files, ImportReport report, CancellationToken ct)
    {
        var resolver = new PlaceRules(request.Map, placeResolver, report);

        if (request.Source == ImportSource.Platser)
        {
            foreach (var folder in files.Where(f => f.Segments.Length > 0).GroupBy(f => ImportMapParser.Collapse(f.Segments[0])))
            {
                var gps = folder.Where(f => f.Metadata is { Latitude: not null, Longitude: not null })
                    .Select(f => (f.Metadata.Latitude!.Value, f.Metadata.Longitude!.Value)).ToList();
                resolver.AddOwnGps(folder.Key, GeoMath.AgreedCentroid(gps), gps.Count, folder.Count());
            }

            foreach (var folder in files.Where(f => f.Segments.Length > 0).GroupBy(f => ImportMapParser.Collapse(f.Segments[0])))
            {
                var hint = await resolver.ResolveAsync(folder.Key, ct);
                foreach (var file in folder) file.PlaceHint = hint;
            }
        }
        else
        {
            // A title naming a known place ("Hässja vid Spetebyhall", "Firande i Lerbo") places the photos in it
            // that carry no GPS of their own.
            var keys = request.Map.Places.Keys.OrderByDescending(k => k.Length).ToList();
            foreach (var file in files)
            {
                var names = file.Segments.Append(file.Album ?? string.Empty);
                var key = keys.FirstOrDefault(k => names.Any(n => ContainsWord(n, k)));
                if (key is not null) file.PlaceHint = await resolver.ResolveAsync(key, ct);
            }
        }

        report.Places.AddRange(resolver.Describe());
    }

    private async Task AdoptAlbumsAsync(ImportRequest request, List<ImportFile> files, ImportReport report, CancellationToken ct)
    {
        foreach (var album in files.Where(f => f.Album is not null).GroupBy(f => f.Album!).ToList())
        {
            var votes = new Dictionary<string, (int Count, AlbumKind? Kind, DateOnly? Date)>(StringComparer.Ordinal);
            foreach (var file in album)
            {
                var canonical = await FindCanonicalAsync(request.PrincipalId, file, ct);
                var (existing, kind, date) = canonical is { SourceAlbum: not null }
                    ? (canonical.SourceAlbum, canonical.SourceAlbumKind, canonical.SourceAlbumDate)
                    : (file.NearCopy?.SourceAlbum, file.NearCopy?.SourceAlbumKind, file.NearCopy?.SourceAlbumDate);
                if (existing is null) continue;
                votes[existing] = (votes.GetValueOrDefault(existing).Count + 1, kind, date);
            }

            if (votes.Count == 0) continue;
            var (winner, (count, winnerKind, winnerDate)) = votes.MaxBy(kv => kv.Value.Count);
            if (count * 2 <= album.Count() || winner == album.Key) continue;
            foreach (var file in album)
                (file.Album, file.AlbumKind, file.AlbumDate) = (winner, winnerKind ?? AlbumKind.Event, winnerDate);
            report.Albums.Add($"Takeout album \"{album.Key}\" folded into \"{winner}\" ({count}/{album.Count()} already there)");
        }
    }

    private static void ResolvePhotographers(ImportRequest request, List<ImportFile> files, ImportReport report)
    {
        var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var cameraName = DtoMapping.CameraName(file.Metadata.Camera) is { } n ? ImportMapParser.Collapse(n) : null;
            if (cameraName is not null) report.Count(report.Cameras, cameraName);

            var (person, via) = PersonFor(request, file, report);
            if (person is null && cameraName is not null && file.Sidecar?.FromSharedAlbum != true)
            {
                if (CameraEntry(request.Map.Cameras, file.Metadata.Camera) is { } cam)
                    (person, via) = (cam, CapturedBySource.CameraOwner);
                else
                    report.UnmappedCameras.Add(cameraName);
            }

            if (person is null) continue;
            switch (person.Kind)
            {
                case PersonRefKind.None:
                    continue;
                case PersonRefKind.Unresolved:
                    unresolved.Add(person.Text);
                    continue;
                case PersonRefKind.Me when request.MeContactId is null:
                    unresolved.Add("me (pass --me-contact)");
                    continue;
            }

            file.CapturedBy = person.Kind == PersonRefKind.Me ? request.MeContactId : person.ContactId;
            file.CapturedBySource = via;
            report.Count(report.Photographers, $"{person.Text} ({via})");
        }

        foreach (var name in unresolved.Order(StringComparer.OrdinalIgnoreCase))
            report.Errors.Add($"unresolved person '{name}' — replace it with a contact id in the map file");
    }

    private static T? CameraEntry<T>(Dictionary<string, T> entries, CameraInfo? camera)
        where T : class
    {
        if (DtoMapping.CameraName(camera) is { } name && entries.TryGetValue(ImportMapParser.Collapse(name), out var byName)) return byName;
        return camera?.Model is { } model && entries.TryGetValue(ImportMapParser.Collapse(model), out var byModel) ? byModel : null;
    }

    /// <summary>The deepest folder below the album with a <c>person</c> entry — by relative path first, then by
    /// name. Unmapped person-looking folders are reported so the map can be completed.</summary>
    private static (PersonRef? Person, CapturedBySource Via) PersonFor(ImportRequest request, ImportFile file, ImportReport report)
    {
        if (request.Source is not (ImportSource.Handelser or ImportSource.Family)) return (null, CapturedBySource.Folder);
        for (var depth = file.Segments.Length - 1; depth > file.AlbumDepth; depth--)
        {
            var path = string.Join('/', file.Segments[..(depth + 1)]);
            var name = ImportMapParser.Collapse(file.Segments[depth]);
            if (request.Map.People.TryGetValue(ImportMapParser.Collapse(path), out var byPath) || request.Map.People.TryGetValue(name, out byPath))
                return (byPath, CapturedBySource.Folder);
        }

        for (var depth = file.AlbumDepth + 1; depth < file.Segments.Length; depth++)
        {
            var segment = file.Segments[depth];
            if (FolderName.Parse(segment).Precision == FolderDatePrecision.None && !segment.All(char.IsDigit))
                report.Count(report.UnmappedSubfolders, string.Join('/', file.Segments[..(depth + 1)]));
        }

        return (null, CapturedBySource.Folder);
    }

    private static void SummariseAlbums(List<ImportFile> files, ImportReport report)
    {
        foreach (var album in files.Where(f => f.Album is not null).GroupBy(f => f.Album!).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var core = CoreSpan.Compute(album
                .Where(f => !CaptureTime.IsApproximate(f.Capture!.Source))
                .Select(f => DateOnly.FromDateTime(f.Capture!.TakenAt.UtcDateTime)));
            var first = album.First();
            var span = core is { } c ? $"{c.From:yyyy-MM-dd}..{c.To:yyyy-MM-dd} ({c.Count}/{album.Count()})" : "no exact dates";
            var folder = first.AlbumDate is { } d ? $" folder {d:yyyy-MM-dd}" : string.Empty;
            report.Albums.Add($"{album.Key} [{first.AlbumKind}] {album.Count()} files, core {span}{folder}");
            if (core is { } cs && first.AlbumDate is { } fd && Math.Abs(cs.From.DayNumber - fd.DayNumber) > 7)
                report.Warnings.Add($"album \"{album.Key}\": folder date {fd:yyyy-MM-dd} but photos start {cs.From:yyyy-MM-dd}");
        }
    }

    private async Task CountExpectedDuplicatesAsync(ImportRequest request, List<ImportFile> files, ImportReport report, CancellationToken ct)
    {
        var seen = new HashSet<(DateTimeOffset, long, string)>();
        foreach (var file in files)
        {
            var key = (file.Capture!.TakenAt, file.SizeBytes, file.ContentType);
            if (!seen.Add(key) || await FindCanonicalAsync(request.PrincipalId, file, ct) is not null)
            {
                report.Duplicates++;
            }
            else if (file.NearCopy is not null)
            {
                report.Duplicates++;
                report.NearCopies++;
            }
        }
    }

    private async Task<PhotoAsset?> FindCanonicalAsync(Guid principalId, ImportFile file, CancellationToken ct)
    {
        var takenAt = file.Capture!.TakenAt;
        return await session.Query<PhotoAsset>()
            .Where(a => a.PrincipalId == principalId
                     && (a.TakenAt == takenAt || a.CaptureTimeOverride!.DerivedTakenAt == takenAt)
                     && a.SizeBytes == file.SizeBytes
                     && a.ContentType == file.ContentType
                     && a.Status != AssetStatus.Duplicate
                     && a.Status != AssetStatus.Declared)
            .FirstOrDefaultAsync(ct);
    }

    private async Task WriteAsync(ImportRequest request, List<ImportFile> files, ImportReport report, CancellationToken ct)
    {
        var deviceId = $"import:{request.Source.ToString().ToLowerInvariant()}";
        var done = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await WriteOneAsync(request, deviceId, file, report, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException)
            {
                report.Errors.Add($"{file.Relative}: {ex.Message}");
            }

            if (++done % 200 == 0) logger.LogInformation("Imported {Done}/{Total}…", done, files.Count);
        }
    }

    private async Task WriteOneAsync(ImportRequest request, string deviceId, ImportFile file, ImportReport report, CancellationToken ct)
    {
        var declare = new DeclarePhotoRequest
        {
            DeviceId = deviceId,
            MediaStoreId = file.Relative,
            ContentType = file.ContentType,
            SizeBytes = file.SizeBytes,
            TakenAt = file.Capture!.TakenAt,
        };
        var facts = new ImportFacts
        {
            TakenAtSource = file.Capture.Source,
            PlaceHint = file.PlaceHint,
            CapturedByContactId = file.CapturedBy,
            CapturedBySource = file.CapturedBy is null ? null : file.CapturedBySource,
            SourceAlbum = file.Album,
            SourceAlbumKind = file.Album is null ? null : file.AlbumKind,
            SourceAlbumDate = file.Album is null ? null : file.AlbumDate,
        };
        if (file.NearCopy is not null && await FindCanonicalAsync(request.PrincipalId, file, ct) is null)
            facts.DuplicateOfId = file.NearCopy.CanonicalId;

        var declared = await declareService.DeclareAsync(request.PrincipalId, declare, facts, ct);
        if (!declared.IsOk)
        {
            report.Errors.Add($"{file.Relative}: {declared.Error}");
            return;
        }

        switch (declared.Value!.Status)
        {
            case AssetStatus.Duplicate:
                report.Duplicates++;
                if (facts.DuplicateOfId is not null) report.NearCopies++;
                return;
            case not AssetStatus.Declared:
                report.AlreadyPresent++;
                return;
        }

        var asset = await session.LoadAsync<PhotoAsset>(declared.Value.AssetId, ct)
            ?? throw new InvalidOperationException($"Declared asset {declared.Value.AssetId} vanished.");
        await using (var content = File.OpenRead(file.FullPath))
            await store.PutAsync(asset.OriginalKey, content, file.SizeBytes, file.ContentType, ct);
        var completed = await completeService.CompleteAsync(request.PrincipalId, asset.Id, ct);
        if (completed.IsOk) report.Imported++;
        else report.Errors.Add($"{file.Relative}: {completed.Error}");
    }

    private static bool ContainsWord(string text, string word) =>
        Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(word)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase);

    [GeneratedRegex(@"-(edited|redigerad)(\(\d+\))?$", RegexOptions.IgnoreCase)]
    private static partial Regex EditedCopy();

    [GeneratedRegex(@"^Photos from \d{4}$", RegexOptions.IgnoreCase)]
    private static partial Regex YearFolder();

    private sealed class ImportFile
    {
        public required string FullPath { get; init; }

        public required string Relative { get; init; }

        public required string[] Segments { get; init; }

        public required string ContentType { get; init; }

        public required AssetKind Kind { get; init; }

        public required long SizeBytes { get; init; }

        public required DateTime FileTimeUtc { get; init; }

        public required MediaMetadata Metadata { get; init; }

        public DateTime? FilenameLocal { get; set; }

        public bool FilenameIsTransfer { get; set; }

        public TakeoutSidecar? Sidecar { get; set; }

        public string? Album { get; set; }

        public AlbumKind AlbumKind { get; set; }

        public int AlbumDepth { get; set; }

        public DateOnly? AlbumDate { get; set; }

        public FolderDatePrecision AlbumPrecision { get; set; }

        public CaptureDecision? Capture { get; set; }

        public PlaceHint? PlaceHint { get; set; }

        public NearCopy? NearCopy { get; set; }

        public Guid? CapturedBy { get; set; }

        public CapturedBySource CapturedBySource { get; set; }
    }
}
