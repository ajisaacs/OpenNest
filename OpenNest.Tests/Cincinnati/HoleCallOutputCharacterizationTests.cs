using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Posts.Cincinnati;

namespace OpenNest.Tests.Cincinnati;

public class HoleCallOutputCharacterizationTests
{
    [Theory]
    [InlineData("part", true)]
    [InlineData("part", false)]
    [InlineData("inline-sheet", true)]
    [InlineData("inline-sheet", false)]
    [InlineData("grouped-sheet", true)]
    [InlineData("grouped-sheet", false)]
    public void SingleHole_NullMap_FirstAndLastFeature_HasExactOutput(string route, bool lineNumbers)
    {
        var config = CreateConfig(lineNumbers);
        var program = CreateProgram(CreateCall(7, -12.34567, 6.78901));
        var write = CreateWriter(route, config, null);
        using var output = new StringWriter { NewLine = "\n" };

        write(output, program);

        var body = FirstHole(route, lineNumbers ? "N73 G52 X-12.35 Y6.79" : "G52 X-12.35 Y6.79")
            + Lines("M98 P7", "G52 X0 Y0");
        Assert.Equal(WrapOutput(route, body, lineNumbers ? 73 : null), output.ToString());
    }

    [Theory]
    [InlineData("part", true)]
    [InlineData("part", false)]
    [InlineData("inline-sheet", true)]
    [InlineData("inline-sheet", false)]
    [InlineData("grouped-sheet", true)]
    [InlineData("grouped-sheet", false)]
    public void MultipleHoles_MappedAndUnmapped_FirstAndLaterIndices_HaveExactOutput(string route, bool lineNumbers)
    {
        var config = CreateConfig(lineNumbers);
        config.InteriorM47 = M47Mode.None;
        // Hole 8 is unmapped and falls back to its call id.
        var write = CreateWriter(route, config, new Dictionary<int, int> { [7] = 901, [9] = 0 });
        var program = CreateProgram(
            CreateCall(7, -12.34567, 6.78901),
            CreateCall(8, 0.00499, -0.006),
            CreateCall(9, 1.1, -2.2)
        );
        using var output = new StringWriter { NewLine = "\n" };

        write(output, program);

        var body = FirstHole(route, lineNumbers ? "N73 G52 X-12.35 Y6.79" : "G52 X-12.35 Y6.79") + Lines(
            "M98 P901",
            "G52 X0 Y0",
            "M47",
            lineNumbers ? "N1002 G52 X0 Y-0.01" : "G52 X0 Y-0.01",
            "M98 P8",
            "G52 X0 Y0",
            "M47",
            lineNumbers ? "N1003 G52 X1.1 Y-2.2" : "G52 X1.1 Y-2.2",
            "M98 P0",
            "G52 X0 Y0"
        );
        Assert.Equal(WrapOutput(route, body, lineNumbers ? 73 : null), output.ToString());
    }

    [Theory]
    [InlineData("part", true)]
    [InlineData("part", false)]
    [InlineData("inline-sheet", true)]
    [InlineData("inline-sheet", false)]
    [InlineData("grouped-sheet", true)]
    [InlineData("grouped-sheet", false)]
    public void HoleBetweenOrdinaryFeatures_PreservesFeatureSequence(string route, bool lineNumbers)
    {
        var config = CreateConfig(lineNumbers);
        var program = CreateProgram(
            new RapidMove(1, 1),
            new LinearMove(2, 1),
            new LinearMove(1, 1),
            CreateCall(7, -12.34567, 6.78901),
            new RapidMove(3, 3),
            new LinearMove(4, 3),
            new LinearMove(3, 3)
        );
        var write = CreateWriter(route, config, new Dictionary<int, int>());
        using var output = new StringWriter { NewLine = "\n" };

        write(output, program);

        var body = Lines(lineNumbers ? "N73 G0 X1 Y1" : "G0 X1 Y1");
        if (route != "part")
            body += Lines("( PART: Sample )");
        body += Lines(
            "(WARNING: No library found)",
            "G84",
            "G1 X2 Y1 F#148",
            "G1 X1 Y1",
            "M35",
            "M47",
            lineNumbers ? "N1002 G52 X-12.35 Y6.79" : "G52 X-12.35 Y6.79",
            "M98 P7",
            "G52 X0 Y0",
            "M47",
            lineNumbers ? "N1003 G0 X3 Y3" : "G0 X3 Y3",
            "(WARNING: No library found)",
            "G84",
            "G1 X4 Y3 F#148",
            "G1 X3 Y3",
            "M35"
        );
        Assert.Equal(WrapOutput(route, body, lineNumbers ? 73 : null), output.ToString());
    }

    [Theory]
    [InlineData("part")]
    [InlineData("inline-sheet")]
    [InlineData("grouped-sheet")]
    public void ConfigChanges_AreLiveExceptConstructionTimePrecision(string route)
    {
        var config = CreateConfig(false);
        var write = CreateWriter(route, config, null);
        var program = CreateProgram(CreateCall(7, -12.34567, 6.78901));
        config.UseLineNumbers = true;
        config.FeatureLineNumberStart = 87;
        config.PostedAccuracy = 5;
        using var first = new StringWriter { NewLine = "\n" };

        write(first, program);

        Assert.Equal(
            WrapOutput(route, FirstHole(route, "N87 G52 X-12.35 Y6.79") + Lines("M98 P7", "G52 X0 Y0"), 87),
            first.ToString()
        );
        config.UseLineNumbers = false;
        using var second = new StringWriter { NewLine = "\n" };
        write(second, program);
        Assert.Equal(
            WrapOutput(route, FirstHole(route, "G52 X-12.35 Y6.79") + Lines("M98 P7", "G52 X0 Y0"), null),
            second.ToString()
        );
    }

    [Theory]
    [InlineData("part")]
    [InlineData("inline-sheet")]
    [InlineData("grouped-sheet")]
    public void HoleWriteFailure_PropagatesWithoutResetOrLaterFeature(string route)
    {
        var write = CreateWriter(route, CreateConfig(true), null);
        var program = CreateProgram(CreateCall(7, -12.34567, 6.78901), CreateCall(8, 1, 2));
        using var output = new RejectHoleCallWriter();

        var error = Assert.Throws<IOException>(() => write(output, program));

        Assert.Same(output.Failure, error);
        Assert.Equal(Header(route, 73) + FirstHole(route, "N73 G52 X-12.35 Y6.79"), output.ToString());
    }

    private static CincinnatiPostConfig CreateConfig(bool lineNumbers) => new()
    {
        PostedAccuracy = 2,
        FeatureLineNumberStart = 73,
        UseLineNumbers = lineNumbers,
        PalletExchange = PalletMode.None,
        KerfCompensation = KerfMode.PreApplied,
        UseAntiDive = false,
    };

    /// <param name="mapping">
    /// Posted sub-program numbers by call id, resolved at write time to the hole programs
    /// the writer actually sees (a part owns copies of its drawing's hole programs).
    /// Writers are still constructed here, so construction-time settings stay fixed.
    /// </param>
    private static Action<TextWriter, Program> CreateWriter(
        string route,
        CincinnatiPostConfig config,
        Dictionary<int, int>? mapping
    )
    {
        var byProgram = mapping == null
            ? null
            : new Dictionary<Program, int>(ReferenceEqualityComparer.Instance);

        void Resolve(Program owner)
        {
            if (byProgram == null)
                return;
            byProgram.Clear();
            foreach (var call in owner.Codes.OfType<SubProgramCall>())
            {
                if (mapping!.TryGetValue(call.Id, out var number))
                    byProgram[call.Program] = number;
            }
        }

        if (route == "part")
        {
            var writer = new CincinnatiPartSubprogramWriter(config, byProgram);
            return (output, program) =>
            {
                Resolve(program);
                writer.Write(output, program, "Sample", 201, "", "", 10);
            };
        }

        var sheetWriter = new CincinnatiSheetWriter(config, new ProgramVariableManager(), byProgram);
        Dictionary<Part, PartSubprogram>? partMapping = route == "grouped-sheet" ? new() : null;
        return (output, program) =>
        {
            var plate = new Plate(48, 96);
            var part = new Part(new Drawing("Sample", program));
            plate.Parts.Add(part);
            Resolve(part.Program);
            sheetWriter.Write(output, plate, "Nest", 1, 101, "", "", partMapping);
        };
    }

    private static Program CreateProgram(params ICode[] codes)
    {
        var program = new Program();
        program.Codes.AddRange(codes);
        foreach (var call in codes.OfType<SubProgramCall>())
            program.SubPrograms[call.Id] = call.Program;
        return program;
    }

    private static SubProgramCall CreateCall(int id, double x, double y)
    {
        var hole = new Program();
        hole.Codes.Add(new LinearMove(1, 0) { Layer = LayerType.Leadin });
        hole.Codes.Add(new ArcMove(new Vector(1, 0), Vector.Zero, RotationType.CW));
        hole.Mode = Mode.Incremental;
        return new SubProgramCall { Id = id, Program = hole, Offset = new Vector(x, y) };
    }

    // Literal snapshots deliberately do not call the production emitter/formatter.
    private static string WrapOutput(string route, string body, int? gotoTarget) => Header(route, gotoTarget) + body +
        (route == "part" ? Lines("M99 (END OF Sample)") : Lines("M42", "M99 (END OF Nest.001)"));

    // The sheet's GOTO names the first feature's line number, posted only with line numbers.
    private static string Header(string route, int? gotoTarget)
    {
        if (route == "part")
            return Lines("(*****************************************************)", ":201", "( PART: Sample )");

        var lines = new List<string>
        {
            "(*****************************************************)",
            "( START OF Nest.001 )",
            ":101",
            "( Layout 1 )",
            "( SHEET NAME = 48 X 96 )",
            "( Total parts on sheet = 1 )",
            "#110=48 (SHEET WIDTH FOR CUTOFFS)",
            "#111=96 (SHEET LENGTH FOR CUTOFFS)",
            "M42",
            "N10000",
            "G92 X#5021 Y#5022",
            "M98 P100 (Variable Declaration)",
            "G90",
            "M47",
        };
        if (gotoTarget != null)
            lines.Add($"GOTO{gotoTarget}( Goto Feature )");
        return Lines(lines.ToArray());
    }

    // A sheet part that opens with a hole call names the part after the call's G52 line.
    private static string FirstHole(string route, string g52Line) =>
        route == "part" ? Lines(g52Line) : Lines(g52Line, "( PART: Sample )");

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    private sealed class RejectHoleCallWriter : StringWriter
    {
        public readonly IOException Failure = new("Rejected hole call");

        public RejectHoleCallWriter() => NewLine = "\n";

        public override void WriteLine(string? value)
        {
            if (value == "M98 P7")
                throw Failure;
            base.WriteLine(value);
        }
    }
}
