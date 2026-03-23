using OpenNest.Posts.Cincinnati;

namespace OpenNest.Tests.Cincinnati;

public class SpeedClassifierTests
{
    [Theory]
    [InlineData(20.0, 10.0, "FAST")]
    [InlineData(5.0, 10.0, "FAST")]
    [InlineData(4.9, 10.0, "MEDIUM")]
    [InlineData(0.5, 10.0, "SLOW")]
    public void Classify_ReturnsExpectedClass(double contourLength, double sheetDiagonal, string expected)
    {
        var classifier = new SpeedClassifier();
        Assert.Equal(expected, classifier.Classify(contourLength, sheetDiagonal));
    }

    [Theory]
    [InlineData(0.8702, 3.927, "CutDist=.8702/3.927")]
    [InlineData(18.9722, 3.927, "CutDist=18.9722/3.927")]
    [InlineData(0.0, 10.0, "CutDist=0/10")]
    public void FormatCutDist_IncludesLengthAndDiagonal(double contour, double diag, string expected)
    {
        var classifier = new SpeedClassifier();
        Assert.Equal(expected, classifier.FormatCutDist(contour, diag));
    }
}
