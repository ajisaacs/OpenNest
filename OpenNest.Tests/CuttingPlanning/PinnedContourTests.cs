using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// D1 / invariant 3: a pinned contour has exactly one entry, its pin's, and is emitted with the
/// pin's own lead-in, lead-out and tab through the existing emitter. Unpinned contours keep the
/// plate's automatic selection; a pin that does not resolve is never guessed (invariant 5).
/// </summary>
public class PinnedContourTests
{
    private static readonly Vector Start = new(-2, -2);

    [Fact]
    public void PinnedOutsideCorner_HasOneEntryEmittedWithThePinsLeadAndTab()
    {
        var drawing = SquareWithPin(new Vector(0, 0), Corner());
        var prepared = Prepare(drawing, NestSettings());
        var perimeter = prepared.PerimeterOrdinal;

        var pinned = Assert.IsType<ContourChoice>(prepared.PinnedEntry(perimeter));
        Assert.Equal(new Vector(0, 0), pinned.Point);
        Assert.Equal(1, prepared.PinNumberOf(perimeter));
        Assert.Same(pinned, prepared.ClosestEntry(perimeter, new Vector(10, 10)));
        Assert.Equal([pinned], prepared.Entries(perimeter, new Vector(10, 10)));
        Assert.Equal([pinned], prepared.AutomaticEntryCandidatesWithFallbacks(perimeter, new Vector(10, 10)).Select(c => c.Choice));
        // Nothing may relocate a pinned entry, not even an exact native point elsewhere on it.
        Assert.Throws<ArgumentException>(() => prepared.Entry(perimeter, 1, new Vector(5, 10)));

        var motions = ExecutionMotionReader.Read(prepared.Emit([pinned]), Vector.Zero, null, default).Motions;
        var lead = Assert.Single(motions, m => m.Layer == LayerType.Leadin);
        Assert.Equal(0.25, lead.Length, 9);
        Assert.Equal(new Vector(0, 0), lead.End);
        Assert.DoesNotContain(motions, m => m.Layer == LayerType.Leadout);
        Assert.Equal(0.03, motions.Last(m => !m.Rapid).End.DistanceTo(new Vector(0, 0)), 6);
    }

    [Fact]
    public void PinnedOutsideCorner_PlansReadyAndIsIndependentlyReplayed()
    {
        var drawing = SquareWithPin(new Vector(0, 0), Corner());
        var part = new Part(drawing, new Vector(5, 5));
        var plate = new Nest().CreatePlate();
        plate.Parts.Add(part);

        var proposal = CuttingPlanBatch.Capture([plate], NestSettings(), false).Plan();

        var planned = Assert.Single(proposal.Plates);
        Assert.True(planned.IsReady, string.Join("\n", proposal.Describe("in")));
        Assert.True(planned.Result.IndependentlyReplayed);
        var placement = Assert.Single(planned.Result.ProposedOrder);
        Assert.Equal(new Vector(0, 0), Assert.Single(placement.ContourChoices).Point);
        var motions = ExecutionMotionReader.Read(placement.CopyProgram(), part.Location, null, default).Motions;
        Assert.Equal(0.25, Assert.Single(motions, m => m.Layer == LayerType.Leadin).Length, 9);
        Assert.Equal(new Vector(5, 5), motions.First(m => m.Layer == LayerType.Leadin).End);
    }

    [Fact]
    public void PinnedHole_BesideAnUnpinnedHole_ThatStaysAutomatic()
    {
        // Holes(): circles at (3, 3) and (7, 3), radius 1, inside a 10 x 10 square.
        var drawing = new Drawing("holes", PreparedContourTests.Holes());
        var pinLead = new LineLeadIn { Length = 0.4, ApproachAngle = 90 };
        drawing.CutPins.Add(new ContourCutPin(new Vector(4, 3), pinLead, new NoLeadOut()));
        var unpinned = new Part(new Drawing("plain", PreparedContourTests.Holes()), new Vector(30, 0));
        var part = new Part(drawing, new Vector(0, 0));
        var settings = NestSettings();

        var pinnedResult = CuttingPlanService.Plan(new CuttingPlanRequest([part], Start, confirmedParameters: settings));
        var plainResult = CuttingPlanService.Plan(new CuttingPlanRequest([unpinned], Start + new Vector(30, 0),
            confirmedParameters: settings));

        Assert.Equal(CuttingPlanStatus.Ready, pinnedResult.Status);
        Assert.True(pinnedResult.IndependentlyReplayed);
        var choices = Assert.Single(pinnedResult.ProposedOrder).ContourChoices;
        Assert.Equal(new Vector(4, 3), Assert.Single(choices, c => c.ContourOrdinal == 0).Point);
        // The other hole and the outside are chosen exactly as for the same drawing without pins.
        var plain = Assert.Single(plainResult.ProposedOrder).ContourChoices;
        Assert.Equal(plain.Where(c => c.ContourOrdinal != 0).Select(c => (c.ContourOrdinal, c.EntityOrdinal, c.Point)),
            choices.Where(c => c.ContourOrdinal != 0).Select(c => (c.ContourOrdinal, c.EntityOrdinal, c.Point)));
        // The pinned hole uses the pin's straight lead; the others use the nest's arc lead-ins.
        var leadIns = ExecutionMotionReader.Read(pinnedResult.ProposedOrder[0].CopyProgram(), Vector.Zero, null, default)
            .Motions.Where(m => m.Layer == LayerType.Leadin).ToArray();
        Assert.Single(leadIns, m => m.Curve.IsLine && System.Math.Abs(m.Length - 0.4) < 1e-9);
        Assert.Equal(2, leadIns.Count(m => !m.Curve.IsLine));
    }

    [Fact]
    public void PinWithTabAndLeadOut_IsRefusedWhenTheDrawingIsAuthored()
    {
        Assert.Throws<ArgumentException>(() => new ContourCutPin(new Vector(0, 0),
            new LineLeadIn { Length = 0.25 }, new LineLeadOut { Length = 0.1 }, new NormalTab { Size = 0.03 }));
    }

    [Fact]
    public void UnresolvedPin_PlansThatContourAutomaticallyWithAnUnverifiedWarning()
    {
        var drawing = SquareWithPin(new Vector(5, 5), Corner()); // Inside the square: on no contour.
        var plain = new Part(new Drawing("plain", ExplicitContourTests.Square(false)), new Vector(5, 5));
        var part = new Part(drawing, new Vector(5, 5));
        var settings = NestSettings();

        var result = CuttingPlanService.Plan(new CuttingPlanRequest([part], Start, confirmedParameters: settings));
        var automatic = CuttingPlanService.Plan(new CuttingPlanRequest([plain], Start, confirmedParameters: settings));

        Assert.Equal(CuttingPlanStatus.BestEffort, result.Status);
        Assert.True(result.IndependentlyReplayed);
        var warning = Assert.Single(result.Findings);
        Assert.Equal(PostVerificationKind.FixedLead, warning.Kind);
        Assert.Equal("Drawing pin 1 no longer matches the geometry; planned with nest settings.", warning.Message);
        Assert.Equal(new FixedLeadWarning(null, 1, null), warning.FixedLead);
        Assert.Equal(ExplicitContourTests.Fingerprint(automatic.ProposedOrder[0].CopyProgram()),
            ExplicitContourTests.Fingerprint(result.ProposedOrder[0].CopyProgram()));
    }

    [Fact]
    public void PinnedTabOnAHole_IsNotHonouredAndWarns()
    {
        var drawing = new Drawing("holes", PreparedContourTests.Holes());
        drawing.CutPins.Add(new ContourCutPin(new Vector(4, 3), new LineLeadIn { Length = 0.2, ApproachAngle = 90 },
            new NoLeadOut(), new NormalTab { Size = 0.03 }));
        var prepared = Prepare(drawing, NestSettings());

        Assert.False(prepared.IsPinned(0));
        Assert.Equal(0, Assert.Single(prepared.RejectedPins).PinIndex);

        var result = CuttingPlanService.Plan(new CuttingPlanRequest([new Part(drawing)], Start,
            confirmedParameters: NestSettings()));
        Assert.Equal(CuttingPlanStatus.BestEffort, result.Status);
        Assert.Contains("only the outside can be tabbed", Assert.Single(result.Findings).Message);
    }

    [Fact]
    public void PinsCanOnlyComeFromResolutionsOfTheSameContours()
    {
        var drawing = SquareWithPin(new Vector(0, 0), Corner());
        var prepared = PreparedContours.Capture(drawing.Program, NestSettings());
        var resolution = Assert.Single(CutPinResolver.Resolve(drawing, 0, prepared));

        Assert.Throws<ArgumentException>(() => PreparedContours.Capture(drawing.Program, NestSettings(),
            [resolution, resolution]));
        Assert.Throws<ArgumentException>(() => PreparedContours.Capture(drawing.Program, NestSettings(),
            [resolution with { ContourOrdinal = 5 }]));
        Assert.Throws<ArgumentException>(() => PreparedContours.Capture(drawing.Program, NestSettings(),
            [resolution with { Point = new Vector(3, 3) }]));
        // No resolved pin: the plain preparation, unchanged.
        Assert.False(PreparedContours.Capture(drawing.Program, NestSettings(),
            [resolution with { Status = CutPinStatus.NotOnContour }]).IsPinned(0));
    }

    internal static ContourCutPin Corner(Vector? point = null) => new(point ?? Vector.Zero,
        new LineLeadIn { Length = 0.25, ApproachAngle = 90 }, new NoLeadOut(), new NormalTab { Size = 0.03 });

    internal static Drawing SquareWithPin(Vector point, ContourCutPin template)
    {
        var drawing = new Drawing("pinned", ExplicitContourTests.Square(false));
        drawing.CutPins.Add(new ContourCutPin(point, template.LeadIn, template.LeadOut, template.Tab));
        return drawing;
    }

    // The nest's own settings: 0.15 arc lead-ins, tabs off.
    internal static CuttingParameters NestSettings() => new()
    {
        ExternalLeadIn = new ArcLeadIn { Radius = 0.15 },
        InternalLeadIn = new ArcLeadIn { Radius = 0.15 },
        ArcCircleLeadIn = new ArcLeadIn { Radius = 0.15 },
        ExternalLeadOut = new NoLeadOut(),
        InternalLeadOut = new NoLeadOut(),
        PierceClearance = 0.05
    };

    private static PreparedContours Prepare(Drawing drawing, CuttingParameters settings)
    {
        var plain = PreparedContours.Capture(drawing.Program, settings);
        return PreparedContours.Capture(drawing.Program, settings, CutPinResolver.Resolve(drawing, 0, plain));
    }
}
