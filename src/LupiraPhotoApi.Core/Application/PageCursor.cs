using System.Text;

namespace LupiraPhotoApi.Application;

/// <summary>Opaque keyset cursor over (TakenAt DESC, Id DESC) — base64 of <c>{ticks}:{guid}</c>.</summary>
internal static class PageCursor
{
    public static string Format(DateTimeOffset takenAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{takenAt.UtcTicks}:{id:N}"));

    public static bool TryParse(string cursor, out (DateTimeOffset TakenAt, Guid Id) value)
    {
        value = default;
        Span<byte> buffer = stackalloc byte[64];
        if (!Convert.TryFromBase64String(cursor, buffer, out var written)) return false;
        var parts = Encoding.UTF8.GetString(buffer[..written]).Split(':');
        if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !Guid.TryParse(parts[1], out var id))
            return false;
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks) return false;
        value = (new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
