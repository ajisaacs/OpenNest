using System.Reflection;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// Capture reads a placement's active drawing pins (not when switched off), a locked or
/// unregenerated part keeps its program with a warning, and a proposal goes stale when the
/// drawing's pins or the part's pins-off flag change after capture.
/// </summary>
public class DrawingPinCaptureTests
{
    [Fact]
    public void PinsOff_PlansThePartWithTheNestSettingsAndNoWarning()
    {
        var (plate, part) = PinnedPlate();
        part.DrawingPinsOff = true;
        var plain = new Part(new Drawing("plain", ExplicitContourTests.Square(false)), part.Location);

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate,
            confirmedParameters: PinnedContourTests.NestSettings()));
        var automatic = CuttingPlanService.Plan(new CuttingPlanRequest([plain],
            confirmedParameters: PinnedContourTests.NestSettings()));

        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.Empty(result.Findings);
        Assert.Equal(ExplicitContourTests.Fingerprint(automatic.ProposedOrder[0].CopyProgram()),
            ExplicitContourTests.Fingerprint(result.ProposedOrder[0].CopyProgram()));
    }

    [Fact]
    public void LockedPartWithActivePins_KeepsItsProgramWithAWarningNotARefusal()
    {
        var (plate, part) = PinnedPlate();
        var prepared = PreparedContours.Capture(part.BaseDrawing.Program, ExplicitContourTests.Parameters());
        var locked = prepared.Emit([prepared.ClosestEntry(0, new Vector(-1, 5))]);
        Assert.True(part.RestoreLeadInProgram(locked, true));
        var program = part.Program;

        var proposal = CuttingPlanBatch.Capture([plate], PinnedContourTests.NestSettings(), false).Plan();

        var planned = Assert.Single(proposal.Plates);
        Assert.True(planned.HasOnlyFixedLeadWarnings);
        var warning = Assert.Single(planned.Result.Findings);
        Assert.Equal(PostVerificationKind.FixedLead, warning.Kind);
        Assert.Equal("Locked lead-ins override drawing pins on part 1; use Plate > Remove Lead-ins, then Plan Cutting, to apply the pins.", warning.Message);
        Assert.False(Assert.Single(planned.Result.ProposedOrder).IsRegenerated);
        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply(acceptWarnings: true).Status);
        Assert.Same(program, part.Program);

        // Switching the pins off for this part leaves nothing to warn about.
        part.DrawingPinsOff = true;
        Assert.True(CuttingPlanBatch.Capture([plate], PinnedContourTests.NestSettings(), false).Plan().CanApply);
    }

    [Fact]
    public void IneligiblePartWithActivePins_IsNotRegeneratedAndWarns()
    {
        var (plate, part) = PinnedPlate();
        var other = new Part(new Drawing("other", ExplicitContourTests.Square(false)), new Vector(30, 0));
        plate.Parts.Add(other);

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate,
            confirmedParameters: PinnedContourTests.NestSettings(), eligibleParts: [other]));

        Assert.Equal(CuttingPlanStatus.BestEffort, result.Status);
        Assert.Equal("Drawing pins on part 1 are not applied: the part is not regenerated.",
            Assert.Single(result.Findings).Message);
    }

    [Fact]
    public void PinnedPartWithEarlierLeadInsFromOtherSettings_IsRegeneratedWithItsPins()
    {
        var (plate, part) = PinnedPlate();
        var earlier = ExplicitContourTests.Parameters();
        var prepared = PreparedContours.Capture(part.BaseDrawing.Program, earlier);
        Assert.True(part.RestoreLeadInProgram(prepared.Emit([prepared.ClosestEntry(0, new Vector(11, 5))]), false));
        part.CuttingParameters = earlier;

        var proposal = CuttingPlanBatch.Capture([plate], PinnedContourTests.NestSettings(), false).Plan();

        Assert.True(proposal.CanApply, string.Join("\n", proposal.Describe("in")));
        var placement = Assert.Single(proposal.Plates[0].Result.ProposedOrder);
        Assert.True(placement.IsRegenerated);
        Assert.Equal(new Vector(0, 0), Assert.Single(placement.ContourChoices).Point);
    }

    [Theory]
    [InlineData("unchanged")]
    [InlineData("pins-off")]
    [InlineData("pin-added")]
    [InlineData("pin-replaced")]
    [InlineData("pin-removed")]
    [InlineData("list-replaced")]
    public void Apply_IsStaleWhenPinsOrPinsOffChangeAfterCapture(string change)
    {
        var (plate, part) = PinnedPlate();
        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate,
            confirmedParameters: PinnedContourTests.NestSettings()));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        var pins = part.BaseDrawing.CutPins;
        switch (change)
        {
            case "pins-off": part.DrawingPinsOff = true; break;
            case "pin-added": pins.Add(PinnedContourTests.Corner(new Vector(10, 10))); break;
            case "pin-replaced": pins[0] = PinnedContourTests.Corner(new Vector(10, 0)); break;
            case "pin-removed": pins.Clear(); break;
            case "list-replaced": part.BaseDrawing.CutPins = [.. pins]; break;
        }

        var commit = CuttingPlanService.Apply([result]);

        Assert.Equal(change == "unchanged" ? CuttingCommitStatus.Applied : CuttingCommitStatus.Stale, commit.Status);
        if (change != "unchanged")
            Assert.Contains("drawing pins", result.Snapshot!.PlateState!.Difference(), StringComparison.Ordinal);
    }

    [Fact]
    public void PinFingerprint_SeesEveryPublicPropertyOfAPin()
    {
        // ContourCutPin is immutable: every public property must come from a constructor
        // argument the fingerprint writes. A new property means extending StateFingerprint.
        var properties = typeof(ContourCutPin).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).Order().ToArray();
        Assert.Equal(["HasTab", "LeadIn", "LeadOut", "Point", "Tab"], properties);
        Assert.Empty(typeof(ContourCutPin).GetFields(BindingFlags.Public | BindingFlags.Instance));

        var baseline = new ContourCutPin(new Vector(1, 2), new LineLeadIn { Length = 0.25 }, new NoLeadOut(),
            new NormalTab { Size = 0.03 });
        ContourCutPin[] variants =
        [
            new(new Vector(1.5, 2), baseline.LeadIn, baseline.LeadOut, baseline.Tab),
            new(new Vector(1, 2.5), baseline.LeadIn, baseline.LeadOut, baseline.Tab),
            new(baseline.Point, new LineLeadIn { Length = 0.3 }, baseline.LeadOut, baseline.Tab),
            new(baseline.Point, new ArcLeadIn { Radius = 0.25 }, baseline.LeadOut, baseline.Tab),
            new(baseline.Point, baseline.LeadIn, baseline.LeadOut, new NormalTab { Size = 0.04 }),
            new(baseline.Point, baseline.LeadIn, baseline.LeadOut, new BreakerTab { Size = 0.03 }),
            new(baseline.Point, baseline.LeadIn, baseline.LeadOut), // No tab.
            new(baseline.Point, baseline.LeadIn, new LineLeadOut { Length = 0.1 }),
        ];
        var fingerprints = variants.Select(v => StateFingerprint.Of([v]))
            .Append(StateFingerprint.Of([baseline])).ToArray();
        Assert.Equal(fingerprints.Length, fingerprints.Distinct().Count());
        Assert.Equal(StateFingerprint.Of([baseline]), StateFingerprint.Of([new ContourCutPin(
            new Vector(1, 2), new LineLeadIn { Length = 0.25 }, new NoLeadOut(), new NormalTab { Size = 0.03 })]));
        Assert.NotEqual(StateFingerprint.Of((IReadOnlyList<ContourCutPin>)null!), StateFingerprint.Of([]));
        Assert.NotEqual(StateFingerprint.Of([baseline, variants[0]]), StateFingerprint.Of([variants[0], baseline]));
    }

    private static (Plate Plate, Part Part) PinnedPlate()
    {
        var part = new Part(PinnedContourTests.SquareWithPin(new Vector(0, 0), PinnedContourTests.Corner()),
            new Vector(5, 5));
        var plate = new Nest().CreatePlate();
        plate.Parts.Add(part);
        return (plate, part);
    }
}
