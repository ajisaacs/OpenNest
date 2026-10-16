using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class ContourCutPinTests
{
    private sealed class DerivedLineLeadIn : LineLeadIn { }

    [Fact]
    public void Pin_OwnsCopiesOfItsLeadsAndTab()
    {
        var leadIn = new LineLeadIn { Length = 0.25, ApproachAngle = 90 };
        var tab = new NormalTab { Size = 0.03 };
        var pin = new ContourCutPin(new Vector(1, 2), leadIn, new NoLeadOut(), tab);

        leadIn.Length = 9;
        tab.Size = 9;
        ((LineLeadIn)pin.LeadIn).Length = 7;
        pin.Tab.Size = 7;

        Assert.Equal(new Vector(1, 2), pin.Point);
        Assert.Equal(0.25, Assert.IsType<LineLeadIn>(pin.LeadIn).Length);
        Assert.Equal(0.03, Assert.IsType<NormalTab>(pin.Tab).Size);
        Assert.IsType<NoLeadOut>(pin.LeadOut);
        Assert.True(pin.HasTab);
    }

    [Fact]
    public void Pin_WithoutTab_KeepsItsLeadOut()
    {
        var pin = new ContourCutPin(Vector.Zero, new ArcLeadIn { Radius = 0.1 }, new LineLeadOut { Length = 0.05 });

        Assert.False(pin.HasTab);
        Assert.Null(pin.Tab);
        Assert.Equal(0.05, Assert.IsType<LineLeadOut>(pin.LeadOut).Length);
    }

    [Fact]
    public void Pin_WithTab_RefusesALeadOut()
    {
        var ex = Assert.Throws<ArgumentException>(() => new ContourCutPin(
            Vector.Zero, new LineLeadIn { Length = 0.25 }, new LineLeadOut { Length = 0.05 }, new NormalTab()));

        Assert.Contains("tab", ex.Message);
    }

    [Fact]
    public void Pin_RefusesInvalidInput()
    {
        var lead = new LineLeadIn { Length = 0.25 };
        Assert.Throws<ArgumentException>(() => new ContourCutPin(new Vector(double.NaN, 0), lead, new NoLeadOut()));
        Assert.Throws<ArgumentException>(() => new ContourCutPin(Vector.Zero, null, new NoLeadOut()));
        Assert.Throws<ArgumentException>(() => new ContourCutPin(Vector.Zero, lead, null));
        Assert.Throws<ArgumentException>(() => new ContourCutPin(
            Vector.Zero, new LineLeadIn { Length = -0.1 }, new NoLeadOut()));
        Assert.Throws<ArgumentException>(() => new ContourCutPin(
            Vector.Zero, lead, new NoLeadOut(), new NormalTab { Size = double.PositiveInfinity }));
    }

    [Fact]
    public void Pin_RefusesTypesThatAreNotBuiltIn()
    {
        Assert.Throws<NotSupportedException>(() => new ContourCutPin(
            Vector.Zero, new DerivedLineLeadIn { Length = 0.25 }, new NoLeadOut()));
    }
}
