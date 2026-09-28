using LupiraPhotoApi.Core.Application.Import;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class FolderNameTests
{
    [Theory]
    [InlineData("2017-10-03 Svalbard", "2017-10-03", FolderDatePrecision.Day, "Svalbard")]
    [InlineData("2020-07 Evelina Segling Maxi77", "2020-07-01", FolderDatePrecision.Month, "Evelina Segling Maxi77")]
    [InlineData("2007 Sommar", "2007-01-01", FolderDatePrecision.Year, "Sommar")]
    [InlineData("Heden 2013-09-09", "2013-09-09", FolderDatePrecision.Day, "Heden")]
    [InlineData("2016-12-24", "2016-12-24", FolderDatePrecision.Day, "")]
    public void Reads_DatedNames(string name, string date, FolderDatePrecision precision, string title)
    {
        var info = FolderName.Parse(name);
        Assert.Equal(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), info.Date);
        Assert.Equal(precision, info.Precision);
        Assert.Equal(title, info.Title);
    }

    [Theory]
    [InlineData("Katter")]
    [InlineData("Sven & Irene")]
    [InlineData("DV kamera")]
    [InlineData("2016-13-40 Nonsense")]
    public void Undated_HasNoPrecision(string name) => Assert.Equal(FolderDatePrecision.None, FolderName.Parse(name).Precision);
}
