using OpenNest.Undo;

namespace OpenNest.Desktop.Tests.Undo;

public class NestFingerprintTests
{
    [Fact]
    public void UnchangedNest_KeepsItsFingerprint()
    {
        var nest = TestNests.OneSquare();

        Assert.Equal(NestFingerprint.Compute(nest), NestFingerprint.Compute(nest));
    }

    [Fact]
    public void MovingAPart_ChangesTheFingerprint()
    {
        var nest = TestNests.OneSquare();
        var before = NestFingerprint.Compute(nest);

        nest.Plates[0].Parts[0].Offset(1, 0);

        Assert.NotEqual(before, NestFingerprint.Compute(nest));
    }

    [Fact]
    public void ChangingARequiredQuantity_ChangesTheFingerprint()
    {
        var nest = TestNests.OneSquare();
        var before = NestFingerprint.Compute(nest);

        nest.Drawings.First().Quantity.Required = 5;

        Assert.NotEqual(before, NestFingerprint.Compute(nest));
    }
}
