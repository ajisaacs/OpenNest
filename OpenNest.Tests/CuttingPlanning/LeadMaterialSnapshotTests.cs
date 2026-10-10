using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class LeadMaterialSnapshotTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundedCornerContact_RoundingAtAdjacentEndpointIsAccepted(bool atSeam)
    {
        // Rounded imported corner: its native tangent contact falls 1.52e-8
        // from the authored arc/line joint. Exercise both consecutive and seam adjacency.
        var program = new Program();
        var start = new Vector(120.509177, 14.879559);
        program.MoveTo(start);
        program.Codes.Add(new ArcMove(new Vector(120.5, 14.9375),
            new Vector(120.6875000024, 14.9374999848), RotationType.CW));
        program.LineTo(120.5, 28.5625);
        program.Codes.Add(new ArcMove(new Vector(120.6875, 28.75),
            new Vector(120.6875, 28.5625), RotationType.CW));
        program.LineTo(127.6875, 28.75);
        program.Codes.Add(new ArcMove(new Vector(127.875, 28.5625),
            new Vector(127.6875, 28.5625), RotationType.CW));
        program.LineTo(127.875, 14.9375);
        program.Codes.Add(new ArcMove(new Vector(127.6875, 14.75),
            new Vector(127.6875, 14.9375), RotationType.CW));
        program.LineTo(120.6875, 14.75);
        program.Codes.Add(new ArcMove(start,
            new Vector(120.6874999967, 14.9375000024), RotationType.CW));
        if (atSeam)
        {
            var first = program.Codes[1];
            program.Codes.RemoveAt(1);
            program.Codes.Add(first);
            program.Codes[0] = new RapidMove(120.5, 14.9375);
        }

        var material = LeadMaterialSnapshot.Capture(program, Vector.Zero);

        Assert.True(material.IsComplete, material.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrossingOrRetracedBoundary_RemainsRejected(bool retraced)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(8, 8);
        if (retraced)
        {
            program.LineTo(4, 4);
            program.LineTo(0, 8);
        }
        else
        {
            program.LineTo(0, 8);
            program.LineTo(8, 0);
        }
        program.LineTo(0, 0);

        var material = LeadMaterialSnapshot.Capture(program, Vector.Zero);

        Assert.False(material.IsComplete);
        Assert.Contains("Material boundaries", material.Reason);
    }

    [Fact]
    public void SeparateTouchingHoles_RemainRejected()
    {
        var part = BestEffortCuttingPlanTests.TouchingContours();

        var material = LeadMaterialSnapshot.Capture(part.BaseDrawing.Program, Vector.Zero);

        Assert.False(material.IsComplete);
        Assert.Contains("Material boundaries", material.Reason);
    }
}
