namespace LupiraPhotoApi.Core.Domain;

/// <summary>The span an album's photos actually cover: the run of capture days (gaps of at most
/// <see cref="MaxGapDays"/>) holding the most photos. A stray photo from another month doesn't stretch it.</summary>
public static class CoreSpan
{
    public const int MaxGapDays = 2;

    public static (DateOnly From, DateOnly To, int Count)? Compute(IEnumerable<DateOnly> days)
    {
        var sorted = days.Order().ToList();
        if (sorted.Count == 0) return null;

        (DateOnly From, DateOnly To, int Count) best = (sorted[0], sorted[0], 1);
        var (from, to, count) = (sorted[0], sorted[0], 1);
        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].DayNumber - to.DayNumber <= MaxGapDays)
            {
                (to, count) = (sorted[i], count + 1);
            }
            else
            {
                (from, to, count) = (sorted[i], sorted[i], 1);
            }

            if (count > best.Count) best = (from, to, count);
        }

        return best;
    }
}
