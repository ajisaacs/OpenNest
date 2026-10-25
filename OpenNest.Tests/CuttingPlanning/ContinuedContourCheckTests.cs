using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// A many-holed part's middle search steps check only the contour they add, continuing the
/// state its prefix left. That must judge each prefix exactly as checking the whole prefix
/// from before the part does: the same findings, numbered the same way, from the same place.
/// </summary>
public class ContinuedContourCheckTests
{
    private const int Side = 9;
    private static readonly Vector Location = new(5.3, 2.7);
    private static readonly Vector Arrival = new(-1, -1);

    [Fact]
    public void ContinuedChecks_MatchFullPrefixChecks_IncludingACrossing()
    {
        var prepared = PreparedContours.Capture(Plate(), Parameters());
        // Row by row, except that hole 1 is cut before hole 0, so the rapid from hole 0 on to
        // hole 2 crosses the completed hole 1 and every later prefix repeats that finding.
        var holes = Enumerable.Range(0, prepared.PerimeterOrdinal).ToList();
        (holes[0], holes[1]) = (holes[1], holes[0]);
        var choices = new List<ContourChoice>();
        var position = Arrival - Location;
        foreach (var hole in holes)
        {
            var choice = prepared.ClosestEntry(hole, position);
            choices.Add(choice);
            position = prepared.HoleCentres()[hole]!.Value;
        }

        ReleasedContourState continued = null;
        Vector departure = default;
        var accumulated = new List<string>();
        for (var length = 1; length <= choices.Count; length++)
        {
            var prefix = choices.Take(length).ToArray();
            var full = ExecutionMotionReader.Read(prepared.EmitPrefix(prefix), Location, Arrival, default);
            var fullChecker = new ReleasedContourState(reportMissingLeadIns: false);
            var fullFindings = fullChecker.Check(full, Arrival, 1).Select(Describe).ToList();

            if (continued == null)
            {
                continued = fullChecker;
                accumulated.AddRange(fullFindings);
            }
            else
            {
                var step = Isolated(prepared, prefix[^1]);
                var added = continued.Continue(step, departure, 1, default);
                Assert.NotNull(added);
                accumulated.AddRange(added.Select(Describe));
                Assert.True(step.DeparturePoint.DistanceTo(full.DeparturePoint) < 1e-9,
                    $"Prefix {length} departs from {step.DeparturePoint}, not {full.DeparturePoint}.");
            }
            Assert.Equal(fullFindings, accumulated);
            departure = full.DeparturePoint;
        }
        Assert.Contains(accumulated, f => f.Contains("contour 1 of part 1"));
    }

    [Fact]
    public void Continue_NeedsAPartLeftClosed()
    {
        var prepared = PreparedContours.Capture(Plate(), Parameters());

        Assert.Null(new ReleasedContourState(reportMissingLeadIns: false)
            .Continue(Isolated(prepared, prepared.ClosestEntry(0, Vector.Zero)), Arrival, 1, default));
    }

    [Fact]
    public void ManyHoledPart_PlansReady_AndReplays()
    {
        var plate = new Nest().CreatePlate();
        plate.Size = new Size(20, 20);
        plate.Parts.Add(new Part(new Drawing("holes", Plate()), Location));

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate,
            expansionBudget: CuttingPlanBatch.PlateBudget(1), confirmedParameters: Parameters()));

        Assert.True(Side * Side + 1 >= 64, "The fixture must take the continued path.");
        Assert.True(result.Status == CuttingPlanStatus.Ready,
            $"{result.Status}: " + string.Join("; ", result.Findings.Take(3).Select(f => f.Message)));
        Assert.True(result.IndependentlyReplayed);
        Assert.Equal(Side * Side + 1, result.ProposedOrder.Single().ContourChoices.Count);
    }

    private static OwnedExecution Isolated(PreparedContours prepared, ContourChoice choice)
    {
        var execution = ExecutionMotionReader.Read(prepared.EmitCandidateForValidation(choice), Location, null, default);
        return execution;
    }

    private static string Describe(OpenNest.Diagnostics.PostVerificationFinding finding) =>
        $"{finding.Kind}: {finding.Message}";

    // A 10 x 10 square with a 9 x 9 grid of holes 0.5 across, one apart.
    private static Program Plate()
    {
        var p = ExplicitContourTests.Square(false);
        for (var row = 1; row <= Side; row++)
            for (var column = 1; column <= Side; column++)
            {
                p.MoveTo(column + 0.25, row);
                p.ArcTo(column + 0.25, row, column, row, RotationType.CCW);
            }
        return p;
    }

    private static CuttingParameters Parameters() => new()
    {
        ExternalLeadIn = new LineLeadIn { Length = 0.2, ApproachAngle = 90 },
        InternalLeadIn = new LineLeadIn { Length = 0.1, ApproachAngle = 90 },
        ExternalLeadOut = new NoLeadOut(),
        InternalLeadOut = new NoLeadOut(),
    };
}
