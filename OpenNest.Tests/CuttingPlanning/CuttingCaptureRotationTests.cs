using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class CuttingCaptureRotationTests
{
    [Theory]
    [InlineData(0.3, false)]
    [InlineData(-0.3, false)]
    [InlineData(0, false)]
    [InlineData(0.3, true)]
    public void SharedDiamond_CaptureAndReadyPreserveOnceRotatedCleanGeometry(double angle, bool incrementalRoot)
    {
        var leaf = Rectangle(1, 2);
        leaf.Mode = Mode.Incremental;
        var left = new Program();
        left.Codes.Add(new SubProgramCall(leaf, 0));
        left.Mode = Mode.Incremental;
        var right = new Program();
        right.Codes.Add(new SubProgramCall(leaf, 0));
        right.Mode = Mode.Incremental;
        var clean = Rectangle(10, 10);
        clean.Codes.Add(new SubProgramCall(left, 0) { Offset = new Vector(3, 3) });
        clean.Codes.Add(new SubProgramCall(right, 0) { Offset = new Vector(7, 3) });
        if (incrementalRoot) clean.Mode = Mode.Incremental;
        var part = new Part(new Drawing("shared diamond", clean));
        part.Rotate(angle);
        part.Location = new Vector(11, 13);
        var before = ExplicitContourTests.Fingerprint(clean);
        var beforeLeaf = ExplicitContourTests.Fingerprint(leaf);
        var beforePlaced = ExplicitContourTests.Fingerprint(part.Program);
        var bounds = part.BoundingBox;
        var quantity = part.BaseDrawing.Quantity.Nested;
        var settings = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.1 },
            InternalLeadIn = new LineLeadIn { Length = 0.1 },
            ExternalLeadOut = new NoLeadOut(),
            InternalLeadOut = new NoLeadOut(),
            PierceClearance = 0.01
        };
        var original = ExecutionMotionReader.ReadSupported(part.Program, part.Location, null);
        var expectedCuts = original.Motions.Where(IsCut).ToArray();
        var once = new Vector(3, 5).Rotate(angle) + part.Location;
        Assert.Contains(expectedCuts, m => m.End.DistanceTo(once) < 1e-8);
        // Retain the review's independent per-parent clone/once-rotation control.
        var legacy = (Program)clean.Clone();
        legacy.Rotate(angle);
        var legacyCuts = ExecutionMotionReader.ReadSupported(legacy, part.Location, null).Motions.Where(IsCut).ToArray();
        Assert.Equal(expectedCuts.Length, legacyCuts.Length);
        Assert.All(Enumerable.Range(0, expectedCuts.Length), i =>
            Assert.True(expectedCuts[i].End.DistanceTo(legacyCuts[i].End) < 1e-8));

        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part], new Vector(9, 13), confirmedParameters: settings));
        var captured = Assert.Single(snapshot.Placements);
        var nominal = captured.Material.Rings.SelectMany(r => r).ToArray();
        Assert.Equal(expectedCuts.Length, nominal.Length);
        Assert.All(Enumerable.Range(0, nominal.Length), i =>
            Assert.True(nominal[i].End.DistanceTo(expectedCuts[i].End) < 1e-8,
                $"Nominal curve {i} moved from {expectedCuts[i].End} to {nominal[i].End}."));
        var ready = CuttingPlanService.Plan(snapshot);
        Assert.True(ready.Status == CuttingPlanStatus.Ready, string.Join("; ", ready.Findings.Select(f => f.Message)));
        Assert.True(ready.IndependentlyReplayed);
        var emitted = ExecutionMotionReader.ReadSupported(Assert.Single(ready.ProposedOrder).CopyProgram(), part.Location, null)
            .Motions.Where(IsCut).ToArray();
        Assert.Equal(expectedCuts.Sum(m => m.Length), emitted.Sum(m => m.Length), 8);
        Assert.All(emitted, move => Assert.Contains(expectedCuts, expected => expected.Curve.SameSupport(move.Curve)
            && expected.Curve.Contains(move.Curve.Start) && expected.Curve.Contains(move.End)));
        Assert.Equal(before, ExplicitContourTests.Fingerprint(clean));
        Assert.Equal(beforeLeaf, ExplicitContourTests.Fingerprint(leaf));
        Assert.Equal(beforePlaced, ExplicitContourTests.Fingerprint(part.Program));
        Assert.Same(leaf, ((SubProgramCall)left.Codes[0]).Program);
        Assert.Same(leaf, ((SubProgramCall)right.Codes[0]).Program);
        Assert.Same(bounds, part.BoundingBox);
        Assert.Equal(quantity, part.BaseDrawing.Quantity.Nested);
    }

    private static bool IsCut(ExecutionMotion motion) => !motion.Rapid && motion.Layer is LayerType.Cut or LayerType.Display;

    private static Program Rectangle(double width, double height)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(0, height);
        program.LineTo(width, height);
        program.LineTo(width, 0);
        program.LineTo(0, 0);
        return program;
    }
}
