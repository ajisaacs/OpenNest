using OpenNest.CNC;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Tests;

public class PartFlagTests
{
    [Fact]
    public void HasManualLeadIns_DefaultsFalse()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        var drawing = new Drawing("test", pgm);
        var part = new Part(drawing);

        Assert.False(part.HasManualLeadIns);
    }

    [Fact]
    public void HasManualLeadIns_CanBeSet()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        var drawing = new Drawing("test", pgm);
        var part = new Part(drawing);

        part.HasManualLeadIns = true;

        Assert.True(part.HasManualLeadIns);
    }
}
