namespace Spek.Net.Tests;

[TestClass]
public sealed class OverviewNavigationTests
{
    [TestMethod]
    public void PanningPreservesTheTimeSpan()
    {
        var range = new TimeRange(30, 40);
        Assert.AreEqual(new TimeRange(65, 75), range.PanTo(70, 120));
        Assert.AreEqual(range.Span, range.PanTo(70, 120).Span, 0.000000001);
    }

    [TestMethod]
    public void PanningClampsAtBothTrackBoundaries()
    {
        var range = new TimeRange(30, 40);
        Assert.AreEqual(new TimeRange(0, 10), range.PanTo(-100, 120));
        Assert.AreEqual(new TimeRange(110, 120), range.PanTo(200, 120));
    }

    [TestMethod]
    public void PanningIntoAShorterTrackUsesTheWholeTrack()
    {
        Assert.AreEqual(new TimeRange(0, 5), new TimeRange(10, 20).PanTo(15, 5));
    }

    [TestMethod]
    public void PanningDoesNotAccumulateZoomDrift()
    {
        var range = new TimeRange(30.12345, 30.2469);
        for (int i = 0; i < 1000; i++) range = range.PanTo(i % 2 == 0 ? 10 : 90, 120);
        Assert.AreEqual(0.12345, range.Span, 0.000000001);
    }

    [TestMethod]
    public void FrequencyPanningPreservesSpanAndClampsAtBothBoundaries()
    {
        var range = new FrequencyRange(6000, 12000);
        Assert.AreEqual(new FrequencyRange(12000, 18000), range.PanTo(15000, 24000));
        Assert.AreEqual(new FrequencyRange(0, 6000), range.PanTo(-1000, 24000));
        Assert.AreEqual(new FrequencyRange(18000, 24000), range.PanTo(30000, 24000));
        Assert.AreEqual(new FrequencyRange(0, 4000), range.PanTo(15000, 4000));
    }

    [TestMethod]
    public void FrequencyPanningDoesNotAccumulateZoomDrift()
    {
        var range = new FrequencyRange(6000.12345, 6123.4567);
        for (int i = 0; i < 1000; i++) range = range.PanTo(i % 2 == 0 ? 1000 : 20000, 24000);
        Assert.AreEqual(123.33325, range.Span, 0.000000001);
    }

    [TestMethod]
    public void InvalidNavigationArgumentsAreRejected()
    {
        var range = new TimeRange(0, 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => range.PanTo(double.NaN, 120));
        Assert.Throws<ArgumentOutOfRangeException>(() => range.PanTo(double.PositiveInfinity, 120));
        Assert.Throws<ArgumentOutOfRangeException>(() => range.PanTo(5, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => range.PanTo(5, double.NaN));
        var frequency = new FrequencyRange(1000, 2000);
        Assert.Throws<ArgumentOutOfRangeException>(() => frequency.PanTo(double.NaN, 24000));
        Assert.Throws<ArgumentOutOfRangeException>(() => frequency.PanTo(1000, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => frequency.PanTo(1000, 0));
    }
}
