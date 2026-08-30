using LupiraPhotoApi.Core.Application;
using LupiraPhotoApi.Core.Domain;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class PageCursorTests
{
    private static readonly DateTimeOffset TakenAt = new(2026, 8, 23, 14, 5, 6, TimeSpan.Zero);

    [Theory]
    [InlineData(PhotoSort.TakenAtDesc)]
    [InlineData(PhotoSort.TakenAtAsc)]
    public void RoundTrips(PhotoSort sort)
    {
        var id = Guid.NewGuid();

        Assert.True(PageCursor.TryParse(PageCursor.Format(sort, TakenAt, id), sort, out var parsed));
        Assert.Equal(TakenAt, parsed.TakenAt);
        Assert.Equal(id, parsed.Id);
    }

    [Fact]
    public void RejectsACursorFromTheOppositeSort()
    {
        // Accepting it would page from the wrong side of the cursor — no error, just a corrupt list.
        var cursor = PageCursor.Format(PhotoSort.TakenAtDesc, TakenAt, Guid.NewGuid());
        Assert.False(PageCursor.TryParse(cursor, PhotoSort.TakenAtAsc, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!!")]
    [InlineData("aGVsbG8=")]
    [InlineData("OTk5OTk5OTk5OTk5OTk5OTk5OTk5OTk5OmZvbw==")]
    public void RejectsGarbage(string cursor)
    {
        Assert.False(PageCursor.TryParse(cursor, PhotoSort.TakenAtDesc, out _));
    }
}
