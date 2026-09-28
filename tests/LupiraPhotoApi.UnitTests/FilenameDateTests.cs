using LupiraPhotoApi.Core.Application.Import;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class FilenameDateTests
{
    [Theory]
    [InlineData("IMG_20190607_102209.jpg", "2019-06-07 10:22:09", false)]
    [InlineData("VID_20171005_100803.3gp", "2017-10-05 10:08:03", false)]
    [InlineData("2016-03-02 15.43.00.jpg", "2016-03-02 15:43:00", false)]
    [InlineData("DSCPDC_0001_BURST20190607122210548_COVER.jpg", "2019-06-07 12:22:10", false)]
    [InlineData("20170218194357.jpg", "2017-02-18 19:43:57", false)]
    [InlineData("4 bröder 2019-05-04.jpg", "2019-05-04 12:00:00", false)]
    [InlineData("photo_2016-03-22_14-57-54.jpg", "2016-03-22 14:57:54", true)]
    [InlineData("IMG-20190607-WA0001.jpg", "2019-06-07 12:00:00", true)]
    public void Reads_CaptureAndTransferNames(string name, string expected, bool transfer)
    {
        Assert.True(FilenameDate.TryParse(name, out var local, out var isTransfer));
        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), local);
        Assert.Equal(transfer, isTransfer);
    }

    [Theory]
    [InlineData("62165882_10158760920807501_4436520003507847168_n.jpg")]
    [InlineData("15831563032094571016416584967186.jpg")]
    [InlineData("DSC_0602.JPG")]
    [InlineData("received_1463499603728637.jpeg")]
    public void Ignores_NamesWithoutADate(string name) => Assert.False(FilenameDate.TryParse(name, out _, out _));
}
