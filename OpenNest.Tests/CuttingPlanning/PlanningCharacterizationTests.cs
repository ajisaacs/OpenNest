using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// Frozen planning output for plates with no drawing pins and no fixed-lead violations: the
/// batch proposals, expansions and every emitted motion. Pins and fixed-lead warnings must
/// leave these byte-identical. Set OPENNEST_PLANNING_DIGESTS to a file path to append the
/// computed digests there.
/// </summary>
public class PlanningCharacterizationTests
{
    [Theory]
    [InlineData("single-triangle", "DFBB853FB852173C65F48A6B774BA2A4FC7742288AA64AC0C2D9FA41A4010BF6")]
    [InlineData("paired-wedges", "176E90F1FDA34C55D99DA04B8B875FDD54EDBE7851E8DDCF300B37FD46B768D2")]
    [InlineData("repeated-ell-fill", "D70498EC959947A9CBBB72C1F4149D091E935DC0338BE232782ECF9C68C1A368")]
    [InlineData("mixed-irregular", "9493724D4DE57ED68C99BC371632E23D90D4661E1E866AA7EEB66F60E3016371")]
    [InlineData("rotation-edge-fit", "7DDE84F94A4EAB3A0F5ADD3D19E8B25A5A227CA446B29E453DE009154CED5C19")]
    [InlineData("multi-sheet", "AFAE1F04CB182DE8749915B2D26C71A2AD96259A16E9AFD10F2B4900D9F76AED")]
    public void SyntheticNest_PlansExactlyAsBefore(string name, string expected)
    {
        var nest = new NestReader(Path.Combine(RepositoryRoot(), "test-data", "synthetic-nests", name + ".nest")).Read();
        var parts = nest.Drawings.OrderBy(d => d.Name, StringComparer.Ordinal)
            .SelectMany(d => Enumerable.Range(0, d.Quantity.Required).Select(i => (Drawing: d, Index: i)))
            .Select((p, i) => Place(p.Drawing, i))
            .ToArray();
        // One plate per part when the fixture is a multi-sheet job; otherwise one plate.
        var plates = name == "multi-sheet"
            ? parts.Select(part => Plate(nest, part)).ToArray()
            : [Plate(nest, parts)];

        Check(name, expected, plates, ExplicitContourTests.Parameters(), preservePartOrder: false);
    }

    [Theory]
    [InlineData("holes-grid", "5410ECC55639AC89E49361D2D65E58C79FF940C65F5513F7E3A54EF04715BB25")]
    [InlineData("holes-grid-kept-order", "07B70B43549E95C65407B24944DA8F7262B784CFC4ABF86241C10DD8272B1780")]
    [InlineData("rounded-circle-leads", "73677429BFC950669D238E834CAE48F54A483759BE4E0BB1437D096F0AF7F3DA")]
    [InlineData("tabbed-perimeters", "6858C09A02D73E4531D0D59B6CD1CFFFA2C9C8BA5F48CF4943D9C4AEC17195BA")]
    [InlineData("locked-among-regenerated", "D7914712F87513E38AA93776886AAFD5025EC89A0D4D7314F900A1163F830019")]
    public void FocusedFixture_PlansExactlyAsBefore(string name, string expected)
    {
        var nest = new Nest();
        var parameters = ExplicitContourTests.Parameters();
        Plate[] plates;
        var preserve = false;
        switch (name)
        {
            case "holes-grid":
            case "holes-grid-kept-order":
                plates = [Plate(nest, Grid(new Drawing("holes", PreparedContourTests.Holes()), 9))];
                preserve = name.EndsWith("kept-order");
                break;
            case "rounded-circle-leads":
                parameters = ExplicitContourTests.Parameters();
                parameters.RoundLeadInAngles = true;
                parameters.LeadInAngleIncrement = 90;
                plates = [Plate(nest, Grid(new Drawing("holes", PreparedContourTests.Holes()), 4))];
                break;
            case "tabbed-perimeters":
                parameters.TabsEnabled = true;
                parameters.TabConfig = new NormalTab { Size = 0.2 };
                plates = [Plate(nest, Grid(new Drawing("square", ExplicitContourTests.Square(false)), 4))];
                break;
            default:
                var parts = Grid(new Drawing("holes", PreparedContourTests.Holes()), 4);
                var prepared = PreparedContours.Capture(parts[1].BaseDrawing.Program, parameters);
                var locked = prepared.Emit([prepared.ClosestEntry(0, new Vector(5, 3)),
                    prepared.ClosestEntry(1, new Vector(9, 3)), prepared.ClosestEntry(2, new Vector(11, 3))]);
                Assert.True(parts[1].RestoreLeadInProgram(locked, true));
                plates = [Plate(nest, parts)];
                break;
        }

        Check(name, expected, plates, parameters, preserve);
    }

    /// <summary>
    /// Red row for D1a: a locked hole lead that runs into the material of a part nested in
    /// that hole. Today the plate is refused and offers no proposal.
    /// </summary>
    [Fact]
    public void LockedHoleLeadIntoNeighbourMaterial_IsRefusedToday()
    {
        var (plate, _, _) = LockedHoleLeadFixture();

        var planned = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan().Plates[0];

        Assert.False(planned.CanApplyWithWarnings, Describe(planned));
        Assert.Empty(planned.Result.ProposedOrder);
        Assert.Contains(planned.Result.Findings, f => f.Message.Contains("another placed material"));
    }

    /// <summary>Red row for D1a: a locked tabbed perimeter has no certified tab and is refused today.</summary>
    [Fact]
    public void LockedTabbedPerimeter_IsRefusedToday()
    {
        var (plate, _) = LockedTabbedFixture();

        var planned = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan().Plates[0];

        Assert.False(planned.CanApplyWithWarnings);
        Assert.Empty(planned.Result.ProposedOrder);
    }

    /// <summary>
    /// A 20 x 20 host with a 10 x 10 cutout and a 2 x 2 part nested in it. The host is locked
    /// with a hole lead-in that starts inside the nested part's material.
    /// </summary>
    internal static (Plate Plate, Part Host, Part Nested) LockedHoleLeadFixture()
    {
        var clean = new Program();
        clean.MoveTo(5, 5); clean.LineTo(15, 5); clean.LineTo(15, 15); clean.LineTo(5, 15); clean.LineTo(5, 5);
        clean.MoveTo(0, 0); clean.LineTo(0, 20); clean.LineTo(20, 20); clean.LineTo(20, 0); clean.LineTo(0, 0);
        var host = new Part(new Drawing("host", clean), new Vector(0, 0));
        var locked = new Program();
        locked.MoveTo(7, 10);
        locked.Codes.Add(new LinearMove(5, 10) { Layer = LayerType.Leadin });
        locked.LineTo(5, 5); locked.LineTo(15, 5); locked.LineTo(15, 15); locked.LineTo(5, 15); locked.LineTo(5, 10);
        locked.MoveTo(-0.3, 10);
        locked.Codes.Add(new LinearMove(0, 10) { Layer = LayerType.Leadin });
        locked.LineTo(0, 20); locked.LineTo(20, 20); locked.LineTo(20, 0); locked.LineTo(0, 0); locked.LineTo(0, 10);
        Assert.True(host.RestoreLeadInProgram(locked, true));
        var nested = new Part(new Drawing("nested", LeadPathValidationTests.Rectangle(0, 0, 2, 2)), new Vector(6, 9));
        var nest = new Nest();
        return (Plate(nest, nested, host), host, nested);
    }

    /// <summary>A single 10 x 10 square locked with a program that leaves a 0.2 tab at its entry.</summary>
    internal static (Plate Plate, Part Part) LockedTabbedFixture()
    {
        var part = new Part(new Drawing("tabbed", ExplicitContourTests.Square(false)), new Vector(1, 1));
        var settings = ExplicitContourTests.Parameters();
        settings.TabsEnabled = true;
        settings.TabConfig = new NormalTab { Size = 0.2 };
        var prepared = PreparedContours.Capture(part.BaseDrawing.Program, settings);
        Assert.True(part.RestoreLeadInProgram(prepared.Emit([prepared.ClosestEntry(0, new Vector(-1, 5))]), true));
        return (Plate(new Nest(), part), part);
    }

    private static string Describe(CuttingPlanPlateResult planned) => planned.Result.Status + ": "
        + string.Join("\n", planned.Result.Findings.Select(f => $"{f.SourceOrdinal} {f.Kind} {f.Message}"));

    internal static void Check(string name, string expected, Plate[] plates, CuttingParameters parameters,
        bool preservePartOrder)
    {
        var proposal = CuttingPlanBatch.Capture(plates, parameters, preservePartOrder).Plan();
        // The digest characterizes real plans: every fixture plate must be Ready.
        Assert.True(proposal.CanApply, string.Join("\n", proposal.Describe("in")));
        var digest = Digest(proposal);
        var output = Environment.GetEnvironmentVariable("OPENNEST_PLANNING_DIGESTS");
        if (!string.IsNullOrEmpty(output))
            File.AppendAllText(output, $"{name} {digest}{Environment.NewLine}");
        Assert.Equal(expected, digest);
    }

    internal static string Digest(CuttingPlanProposal proposal)
    {
        var text = new StringBuilder();
        foreach (var plate in proposal.Plates)
        {
            var result = plate.Result;
            text.Append("plate ").Append(plate.PlateNumber).Append(' ').Append(result.Status)
                .Append(' ').Append(result.IndependentlyReplayed).Append(' ').Append(result.Expansions)
                .Append(' ').Append(Number(result.RapidDistance)).Append(' ').Append(plate.KeptCurrentOrder).Append('\n');
            foreach (var placement in result.ProposedOrder)
            {
                text.Append("part ").Append(placement.SourceOrdinal).Append(' ').Append(placement.IsRegenerated)
                    .Append(' ').Append(string.Join(",", placement.ContourChoices.Select(c =>
                        $"{c.ContourOrdinal}/{c.EntityOrdinal}/{Number(c.Point.X)}/{Number(c.Point.Y)}"))).Append('\n');
                var execution = ExecutionMotionReader.Read(placement.CopyProgram(), placement.Location, null, default);
                foreach (var motion in execution.Motions)
                    text.Append(motion.Rapid ? 'R' : 'C').Append(' ').Append(motion.Layer).Append(' ')
                        .Append(Number(motion.End.X)).Append(' ').Append(Number(motion.End.Y)).Append(' ')
                        .Append(Number(motion.Length)).Append('\n');
            }
            foreach (var finding in result.Findings)
                text.Append("finding ").Append(finding.SourceOrdinal).Append(' ').Append(finding.OtherSourceOrdinal)
                    .Append(' ').Append(finding.Kind).Append(' ').Append(finding.Message).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    // Six decimals keeps the digest independent of last-bit libm differences between platforms.
    private static string Number(double value)
    {
        var rounded = System.Math.Round(value, 6);
        return (rounded == 0 ? 0 : rounded).ToString("F6", CultureInfo.InvariantCulture);
    }

    private static Part Place(Drawing drawing, int index)
    {
        var part = new Part(drawing);
        // Every other part turns a quarter so rotated capture is covered too.
        if (index % 2 == 1)
            part.Rotate(OpenNest.Math.Angle.HalfPI);
        var box = part.BoundingBox;
        part.Offset(index * 25 + 1 - box.X, 1 - box.Y);
        return part;
    }

    private static Part[] Grid(Drawing drawing, int count)
    {
        var side = (int)System.Math.Ceiling(System.Math.Sqrt(count));
        return Enumerable.Range(0, count)
            .Select(i => new Part(drawing, new Vector(1 + i % side * 11, 1 + i / side * 11)))
            .ToArray();
    }

    private static Plate Plate(Nest nest, params Part[] parts)
    {
        var plate = nest.CreatePlate();
        plate.Size = new Size(300, 300);
        foreach (var part in parts)
            plate.Parts.Add(part);
        return plate;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "OpenNest.sln")))
                return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
}
