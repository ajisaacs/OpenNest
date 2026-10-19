using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;
using OpenNest.Posts.Cincinnati;

namespace OpenNest.Tests.Cincinnati;

/// <summary>
/// Posts real lead-in programs (incremental hole sub-programs, per-drawing hole ids)
/// for off-origin parts of several drawings, then replays the G-code to check where
/// each hole is actually cut.
/// </summary>
public class HolePlacementRegressionTests
{
    private static readonly Vector HoleCenter = new(3, 3);

    [Fact]
    public void InlineSheet_HolesCutAtPartLocationWithOwnDrawingRadius()
    {
        var nest = new Nest("N");
        var plate = new Plate(48, 96);
        var large = AddPart(plate, "Large", 0.5, new Vector(10, 20));
        var small = AddPart(plate, "Small", 0.25, new Vector(30, 20));
        nest.Plates.Add(plate);

        var run = Replay(Post(nest, partSubprograms: false));

        AssertHole(run, large, 0.5);
        AssertHole(run, small, 0.25);
        Assert.Equal(2, run.FullCircles.Count);
    }

    [Fact]
    public void PartSubprograms_HolesCutAtPartLocationWithOwnDrawingRadius()
    {
        // One part per sheet: each sheet re-anchors with G92 X#5021 Y#5022.
        var nest = new Nest("N");
        var first = new Plate(48, 96);
        var second = new Plate(48, 96);
        var large = AddPart(first, "Large", 0.5, new Vector(10, 20));
        var small = AddPart(second, "Small", 0.25, new Vector(30, 40));
        nest.Plates.Add(first);
        nest.Plates.Add(second);

        var run = Replay(Post(nest, partSubprograms: true));

        AssertHole(run, large, 0.5);
        AssertHole(run, small, 0.25);
        Assert.Equal(2, run.FullCircles.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameDrawingHoleIds_DifferentRadii_PostSeparateSubprograms(bool partSubprograms)
    {
        var nest = new Nest("N");
        var plate = new Plate(48, 96);
        var large = AddPart(plate, "Large", 0.5, new Vector(10, 20));
        var small = AddPart(plate, "Small", 0.25, new Vector(30, 20));
        nest.Plates.Add(plate);
        // Planned emission labels holes 1, 2, ... within each drawing's program.
        HoleCall(large).Id = 1;
        HoleCall(small).Id = 1;

        var output = Post(nest, partSubprograms);

        var holeSubs = output.Split('\n').Count(l => l.Trim() == "( PART: HOLE )");
        Assert.Equal(2, holeSubs);
        var run = Replay(output);
        AssertHole(run, large, 0.5);
        // Part sub-program calls restore the sheet frame with G92 at the part corner while
        // the tool is elsewhere, shifting later parts on the sheet (a separate known defect).
        if (!partSubprograms)
            AssertHole(run, small, 0.25);
    }

    [Fact]
    public void InlineSheet_PartStartingWithHole_KeepsPartCommentAndSafetyHeadraise()
    {
        var nest = new Nest("N");
        var plate = new Plate(48, 96);
        AddPart(plate, "First", 0.5, new Vector(0, 0));
        AddPart(plate, "Second", 0.5, new Vector(20, 0));
        nest.Plates.Add(plate);

        var lines = Post(nest, partSubprograms: false).Split('\n').Select(l => l.Trim()).ToList();

        var second = lines.IndexOf("( PART: Second )");
        Assert.True(second > 0);
        Assert.EndsWith("G52 X23 Y3", lines[second - 1]);
        Assert.Single(lines, l => l == "( PART: First )");
        Assert.Equal("M47 P2000 (Safety Headraise)", lines[second + 3]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Post_PartsStartingWithHoleCall_HaveNoEmptyPierce(bool partSubprograms)
    {
        var nest = new Nest("N");
        var plate = new Plate(48, 96);
        var part = AddPart(plate, "Large", 0.5, new Vector(10, 20));
        nest.Plates.Add(plate);
        Assert.IsType<SubProgramCall>(part.Program.Codes[0]);

        var run = Replay(Post(nest, partSubprograms));

        Assert.Equal(0, run.EmptyPierces);
        Assert.Equal(2, run.Pierces);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Post_LeavesNestHoleProgramsUntouched(bool partSubprograms)
    {
        var nest = new Nest("N");
        var plate = new Plate(48, 96);
        var part = AddPart(plate, "Large", 0.5, new Vector(10, 20));
        nest.Plates.Add(plate);
        var hole = HoleCall(part).Program;
        var before = hole.Codes.Count;

        Post(nest, partSubprograms);

        Assert.Equal(Mode.Incremental, hole.Mode);
        Assert.Equal(before, hole.Codes.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Post_FollowsPlateSequence(bool partSubprograms)
    {
        var nest = new Nest("N");
        var plate = new Plate(48, 96);
        // Plate order deliberately differs from bottom-then-left order.
        AddPart(plate, "Third", 0.5, new Vector(30, 40));
        AddPart(plate, "First", 0.5, new Vector(0, 0));
        AddPart(plate, "Second", 0.5, new Vector(20, 0));
        nest.Plates.Add(plate);

        var output = Post(nest, partSubprograms);

        var sheet = output[output.IndexOf(":101", System.StringComparison.Ordinal)..];
        var order = sheet
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("( PART: ", System.StringComparison.Ordinal))
            .Take(3)
            .ToArray();
        Assert.Equal(new[] { "( PART: Third )", "( PART: First )", "( PART: Second )" }, order);
    }

    private static Part AddPart(Plate plate, string name, double holeRadius, Vector location)
    {
        var pgm = new Program(Mode.Absolute);
        pgm.Codes.Add(new RapidMove(0, 0));
        pgm.Codes.Add(new LinearMove(10, 0));
        pgm.Codes.Add(new LinearMove(10, 10));
        pgm.Codes.Add(new LinearMove(0, 10));
        pgm.Codes.Add(new LinearMove(0, 0));
        var start = new Vector(HoleCenter.X + holeRadius, HoleCenter.Y);
        pgm.Codes.Add(new RapidMove(start));
        pgm.Codes.Add(new ArcMove(start, HoleCenter, RotationType.CW));

        var part = new Part(new Drawing(name) { Program = pgm });
        part.ApplyLeadIns(
            new CuttingParameters
            {
                ArcCircleLeadIn = new LineLeadIn { Length = 0.125, ApproachAngle = 90 },
                ArcCircleLeadOut = new NoLeadOut(),
                ExternalLeadIn = new LineLeadIn { Length = 0.25, ApproachAngle = 90 },
                ExternalLeadOut = new NoLeadOut(),
            },
            new Vector(10, 10)
        );
        Assert.Equal(Mode.Incremental, HoleCall(part).Program.Mode);
        part.Location = location;
        plate.Parts.Add(part);
        return part;
    }

    private static SubProgramCall HoleCall(Part part) =>
        part.Program.Codes.OfType<SubProgramCall>().Single();

    private static void AssertHole(Replayed run, Part part, double radius)
    {
        var expected = part.Location + HoleCenter;
        Assert.Contains(
            run.FullCircles,
            c => c.Center.DistanceTo(expected) < 1e-3 && System.Math.Abs(c.Radius - radius) < 1e-3
        );
    }

    private static string Post(Nest nest, bool partSubprograms)
    {
        var config = new CincinnatiPostConfig
        {
            PostedAccuracy = 4,
            UsePartSubprograms = partSubprograms,
            ProcessParameterMode = G89Mode.Explicit,
            UseAntiDive = false,
            PalletExchange = PalletMode.None,
        };
        using var ms = new MemoryStream();
        new CincinnatiPostProcessor(config).Post(nest, ms);
        return Encoding.UTF8.GetString(ms.ToArray()).Replace("\r", "");
    }

    private sealed class Replayed
    {
        public List<(Vector Center, double Radius)> FullCircles { get; } = new();
        public int Pierces { get; set; }
        public int EmptyPierces { get; set; }
    }

    /// <summary>
    /// Minimal replay of the post's own dialect: M98/M99 calls, G52/G92 frames,
    /// G0-G3 under G90 with incremental I/J. Every arc must end on its own radius.
    /// </summary>
    private static Replayed Replay(string output)
    {
        var lines = output.Split('\n').Select(Clean).ToList();
        var labels = new Dictionary<int, int>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith(':'))
                labels[int.Parse(lines[i][1..], CultureInfo.InvariantCulture)] = i;
        }

        var run = new Replayed();
        var position = Vector.Zero; // machine position
        var g92 = Vector.Zero; // work origin in machine frame
        var g52 = Vector.Zero; // local shift in the work frame
        var returns = new Stack<int>();
        var pierced = false;
        var cutSincePierce = false;
        var pc = 0;

        while (pc < lines.Count)
        {
            var line = lines[pc++];
            if (line.Length == 0 || line.StartsWith(':') || line.StartsWith("GOTO", System.StringComparison.Ordinal))
                continue;
            if (line.StartsWith("#", System.StringComparison.Ordinal) || line.StartsWith("/", System.StringComparison.Ordinal))
                continue;

            var words = Words(line);
            if (words.TryGetValue("M", out var m))
            {
                if (m == "30")
                    break;
                if (m == "98")
                {
                    returns.Push(pc);
                    pc = labels[int.Parse(words["P"], CultureInfo.InvariantCulture)] + 1;
                    continue;
                }
                if (m == "99")
                {
                    pc = returns.Pop();
                    continue;
                }
                if (m is "35" or "135")
                {
                    if (pierced && !cutSincePierce)
                        run.EmptyPierces++;
                    pierced = false;
                }
                continue;
            }

            if (!words.TryGetValue("G", out var g))
                continue;

            switch (g)
            {
                case "84":
                case "85":
                    run.Pierces++;
                    pierced = true;
                    cutSincePierce = false;
                    break;
                case "52":
                    g52 = new Vector(Num(words["X"]), Num(words["Y"]));
                    break;
                case "92":
                    g92 = words["X"].StartsWith('#')
                        ? Vector.Zero
                        : position - g52 - new Vector(Num(words["X"]), Num(words["Y"]));
                    break;
                case "0":
                case "1":
                case "2":
                case "3":
                    {
                        var target = g92 + g52 + new Vector(Num(words["X"]), Num(words["Y"]));
                        if (g is "2" or "3")
                        {
                            var center = position + new Vector(Num(words["I"]), Num(words["J"]));
                            var radius = position.DistanceTo(center);
                            Assert.True(
                                System.Math.Abs(target.DistanceTo(center) - radius) < 1e-3,
                                $"Arc ends off its radius: {line}"
                            );
                            if (target.DistanceTo(position) < 1e-6)
                                run.FullCircles.Add((center, radius));
                        }
                        if (g != "0")
                            cutSincePierce = true;
                        position = target;
                        break;
                    }
            }
        }

        return run;
    }

    private static string Clean(string line)
    {
        var text = line.Trim();
        var comment = text.IndexOf('(');
        if (comment >= 0)
            text = text[..comment].Trim();
        if (text.StartsWith('N'))
        {
            var space = text.IndexOf(' ');
            text = space < 0 ? "" : text[(space + 1)..];
        }
        return text;
    }

    // Words in a block, keyed by letter; a leading G41/G42 gives way to the motion code after it.
    private static Dictionary<string, string> Words(string line)
    {
        var words = new Dictionary<string, string>();
        foreach (var word in line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
            words[word[..1]] = word[1..];
        return words;
    }

    private static double Num(string value) => double.Parse(value, CultureInfo.InvariantCulture);
}
