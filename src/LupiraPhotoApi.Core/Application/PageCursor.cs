using System.Text;
using LupiraPhotoApi.Core.Domain;

namespace LupiraPhotoApi.Core.Application;

/// <summary>Opaque keyset cursor over (TakenAt, Id) — base64 of <c>{sort}:{ticks}:{guid}</c>.
///
/// The sort tag is load-bearing: the same cursor paged under the opposite direction would silently
/// return the rows on the wrong side of it — no error, just a corrupt list — so a mismatch is rejected.</summary>
internal static class PageCursor
{
    public static string Format(PhotoSort sort, DateTimeOffset takenAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{(int)sort}:{takenAt.UtcTicks}:{id:N}"));

    /// <summary>Parses and validates the cursor against the sort it is being used with.
    /// Returns false for malformed input AND for a cursor minted under a different sort.</summary>
    public static bool TryParse(string cursor, PhotoSort sort, out (DateTimeOffset TakenAt, Guid Id) value)
    {
        value = default;
        Span<byte> buffer = stackalloc byte[96];
        if (!Convert.TryFromBase64String(cursor, buffer, out var written)) return false;
        var parts = Encoding.UTF8.GetString(buffer[..written]).Split(':');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out var sortTag) || sortTag != (int)sort) return false;
        if (!long.TryParse(parts[1], out var ticks) || !Guid.TryParse(parts[2], out var id)) return false;
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks) return false;
        value = (new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
