using LupiraPhotoApi.Application;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class PageCursorTests
{
    [Fact]
    public void RoundTrips()
    {
        var takenAt = new DateTimeOffset(2026, 8, 23, 14, 5, 6, TimeSpan.Zero);
        var id = Guid.NewGuid();

        Assert.True(PageCursor.TryParse(PageCursor.Format(takenAt, id), out var parsed));
        Assert.Equal(takenAt, parsed.TakenAt);
        Assert.Equal(id, parsed.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!!")]
    [InlineData("aGVsbG8=")]
    [InlineData("OTk5OTk5OTk5OTk5OTk5OTk5OTk5OTk5OmZvbw==")]
    public void RejectsGarbage(string cursor)
    {
        Assert.False(PageCursor.TryParse(cursor, out _));
    }
}
