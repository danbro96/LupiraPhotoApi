using LupiraPhotoApi.Core.Domain;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class BboxTests
{
    [Fact]
    public void ParsesMapLibreBoundsOrder()
    {
        Assert.True(Bbox.TryParse("17.5,59.0,18.5,59.6", out var b));
        Assert.Equal(new Bbox(17.5, 59.0, 18.5, 59.6), b);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1,2,3")]
    [InlineData("a,b,c,d")]
    [InlineData("18.5,59.0,17.5,59.6")]
    [InlineData("17.5,59.6,18.5,59.0")]
    [InlineData("-181,0,0,1")]
    [InlineData("0,-91,1,0")]
    public void RejectsMalformedOrInverted(string? value)
    {
        Assert.False(Bbox.TryParse(value, out _));
    }
}
