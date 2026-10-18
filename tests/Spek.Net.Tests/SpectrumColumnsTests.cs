namespace Spek.Net.Tests;

[TestClass]
public sealed class SpectrumColumnsTests
{
    [TestMethod]
    public void AveragesEnergyAndIncludesSilentMeasurements()
    {
        float result = 0;
        var columns = new SpectrumColumns(1, 1, 2, 0, (_, values) => result = values[0]);
        columns.Add(0, 1, [1f]);
        columns.Add(1, 2, [0f]);
        Assert.AreEqual(-3.0103, result, 0.0001);
        Assert.AreEqual(1, columns.ColumnsDone);
    }

    [TestMethod]
    public void WeightsPartialCellsByTheirTimeCoverage()
    {
        var results = new float[3];
        var columns = new SpectrumColumns(3, 1, 4, 10, (x, values) => results[x] = values[0]);
        columns.Add(8, 11, [1f]);
        columns.Add(11, 13, [0.1f]);
        columns.Add(13, 15, [0.01f]);
        Assert.AreEqual(10 * Math.Log10(0.775), results[0], 0.0001);
        Assert.AreEqual(-10, results[1], 0.0001);
        Assert.AreEqual(10 * Math.Log10(0.0325), results[2], 0.0001);
    }

    [TestMethod]
    public void MorePixelsThanSamplesStillHavePositiveTimeCoverage()
    {
        var results = new float[5];
        var columns = new SpectrumColumns(5, 1, 2, 0, (x, values) => results[x] = values[0]);
        columns.Add(0, 1, [1f]);
        columns.Add(1, 2, [0.01f]);
        Assert.AreEqual(5, columns.ColumnsDone);
        Assert.AreEqual(0, results[0], 0.0001);
        Assert.AreEqual(0, results[1], 0.0001);
        Assert.AreEqual(10 * Math.Log10(0.505), results[2], 0.0001);
        Assert.AreEqual(-20, results[3], 0.0001);
        Assert.AreEqual(-20, results[4], 0.0001);
    }

    [TestMethod]
    public void CompleteDoesNotInventMissingAudio()
    {
        var results = new List<float>();
        var columns = new SpectrumColumns(10, 1, 100, 0, (_, values) => results.Add(values[0]));
        columns.Add(0, 23, [0.1f]);
        columns.Complete();
        columns.Complete();
        Assert.AreEqual(3, results.Count);
        Assert.IsTrue(results.All(level => Math.Abs(level + 10) < 0.0001));
    }

    [TestMethod]
    public void CellsOutsideTheViewDoNotCreateColumns()
    {
        var columns = new SpectrumColumns(2, 1, 10, 10, (_, _) => Assert.Fail("Unexpected output."));
        columns.Add(0, 5, [1f]);
        columns.Add(20, 25, [1f]);
        columns.Complete();
        Assert.AreEqual(0, columns.ColumnsDone);
    }
}
