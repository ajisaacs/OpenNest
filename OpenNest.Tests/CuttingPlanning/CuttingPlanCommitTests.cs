using System.Reflection;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Collections;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class CuttingPlanCommitTests
{
    [Fact]
    public void Apply_FixedProgramPlan_ReordersWithoutAccountingEventsAndPublishesOnce()
    {
        var (nest, plate, parts) = FixedPlate();
        var programs = parts.Select(p => p.Program).ToArray();
        var quantities = parts.Select(p => p.BaseDrawing.Quantity.Nested).ToArray();
        var events = Watch(plate);
        Assert.Contains(PostVerificationAnalyzer.Analyze(nest, Vector.Zero).Findings,
            f => f.Kind == PostVerificationKind.RapidCrossing);

        var result = CuttingPlanService.Plan(new CuttingPlanRequest(plate));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.Equal(parts, plate.Parts); // Planning never mutates.
        var commit = CuttingPlanService.Apply([result]);

        Assert.Equal(CuttingCommitStatus.Applied, commit.Status);
        Assert.Empty(commit.RefreshErrors);
        Assert.Equal(new[] { parts[1], parts[0], parts[2] }, plate.Parts);
        Assert.Equal((0, 0, 1), events());
        Assert.Equal(quantities, parts.Select(p => p.BaseDrawing.Quantity.Nested));
        Assert.Equal(programs, parts.Select(p => p.Program)); // Fixed programs are not replaced.
        Assert.DoesNotContain(PostVerificationAnalyzer.Analyze(nest, Vector.Zero).Findings,
            f => f.Kind == PostVerificationKind.RapidCrossing);

        // The same proposal is bound to the captured state, which no longer exists.
        var again = CuttingPlanService.Apply([result]);
        Assert.Equal(CuttingCommitStatus.Stale, again.Status);
        Assert.Equal(new[] { parts[1], parts[0], parts[2] }, plate.Parts);
        Assert.Equal((0, 0, 1), events());
    }

    [Fact]
    public void Apply_RegeneratedPlan_InstallsTheExactReplayedProgramWithOwnedSettings()
    {
        var (nest, plate, part, _) = RegeneratedPlate(Vector.Zero);
        var location = part.Location;
        var rotation = part.Rotation;
        var quantity = part.BaseDrawing.Quantity.Nested;
        // Caller-confirmed settings are planning input, not plate state: editing them after
        // capture neither stales the plan nor leaks into what is installed.
        var parameters = ExplicitContourTests.Parameters();
        var length = ((LineLeadIn)parameters.ExternalLeadIn).Length;
        var result = CuttingPlanService.Plan(new CuttingPlanRequest(plate, confirmedParameters: parameters));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        var proposal = Assert.Single(result.ProposedOrder);
        Assert.True(proposal.IsRegenerated);
        ((LineLeadIn)parameters.ExternalLeadIn).Length = length * 3;

        var commit = CuttingPlanService.Apply([result]);

        Assert.Equal(CuttingCommitStatus.Applied, commit.Status);
        Assert.True(ProgramContent.Equal(proposal.CopyProgram(), part.Program));
        Assert.NotSame(parameters, part.CuttingParameters);
        Assert.Equal(length, ((LineLeadIn)part.CuttingParameters.ExternalLeadIn).Length);
        Assert.True(part.HasManualLeadIns);
        Assert.False(part.LeadInsLocked);
        Assert.Equal(BitConverter.DoubleToInt64Bits(location.X), BitConverter.DoubleToInt64Bits(part.Location.X));
        Assert.Equal(BitConverter.DoubleToInt64Bits(location.Y), BitConverter.DoubleToInt64Bits(part.Location.Y));
        Assert.Equal(BitConverter.DoubleToInt64Bits(rotation), BitConverter.DoubleToInt64Bits(part.Rotation));
        Assert.Equal(quantity, part.BaseDrawing.Quantity.Nested);
        var bounds = part.Program.BoundingBox();
        bounds.Offset(part.Location);
        Assert.Equal((bounds.X, bounds.Y, bounds.Width, bounds.Length),
            (part.BoundingBox.X, part.BoundingBox.Y, part.BoundingBox.Width, part.BoundingBox.Length));
        // The installed program is owned: later proposal copies cannot alias it.
        var detached = proposal.CopyProgram();
        detached.Codes.Clear();
        Assert.NotEmpty(part.Program.Codes);
        Assert.Empty(PostVerificationAnalyzer.Analyze(nest, Vector.Zero).Findings);
    }

    [Theory]
    [InlineData("order")]
    [InlineData("pose")]
    [InlineData("program-in-place")]
    [InlineData("program-replaced")]
    [InlineData("lock")]
    [InlineData("quantity")]
    [InlineData("cutoff")]
    [InlineData("same-name-drawing")]
    [InlineData("list-replaced")]
    [InlineData("added")]
    [InlineData("cutoff-classification")]
    [InlineData("part-settings-in-place")]
    [InlineData("part-settings-nested")]
    [InlineData("plate-settings-in-place")]
    [InlineData("plate-settings-replaced")]
    public void Apply_AnyChangeAfterCapture_IsStaleAndChangesNothing(string change)
    {
        var (_, plate, parts) = FixedPlate();
        var cutOff = new CutOff(new Vector(30, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(cutOff);
        plate.CuttingParameters = new CuttingParameters();
        var result = CuttingPlanService.Plan(new CuttingPlanRequest(plate));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        switch (change)
        {
            case "order": plate.Parts.Reorder([parts[2], parts[1], parts[0]]); break;
            case "pose": parts[0].Offset(1e-12, 0); break;
            case "program-in-place": ((Motion)parts[1].Program.Codes[1]).Feedrate = 7; break;
            case "program-replaced":
                Assert.True(parts[1].RestoreLeadInProgram((Program)parts[1].Program.Clone(), false)); break;
            case "lock": parts[2].LeadInsLocked = true; break;
            case "quantity": plate.Quantity = 2; break;
            case "cutoff":
                cutOff.Position = new Vector(BitConverter.Int64BitsToDouble(
                BitConverter.DoubleToInt64Bits(30.0) + 1), 0); break;
            // Every fixture drawing has the same name: identity must be by reference.
            case "same-name-drawing": parts[2].BaseDrawing.Program.Codes.Add(new Comment("edited")); break;
            case "list-replaced":
                var list = new ObservableList<Part>();
                foreach (var part in parts) list.Add(part);
                plate.Parts = list; break;
            case "added": plate.Parts.Add(Rectangle(20, 0, 2, 2)); break;
            // Classification decides lead, material, obstacle and dependency treatment.
            case "cutoff-classification": parts[0].BaseDrawing.IsCutOff = true; break;
            // A regenerated part would otherwise overwrite an edit made after capture.
            case "part-settings-in-place": parts[0].CuttingParameters.Kerf = 0.125; break;
            case "part-settings-nested": parts[0].CuttingParameters.Assignment.Preference = "LIAT"; break;
            case "plate-settings-in-place": plate.CuttingParameters.PierceClearance = 0.25; break;
            case "plate-settings-replaced": plate.CuttingParameters = new CuttingParameters(); break;
        }
        var after = PlateCuttingState.Capture(plate);
        var events = Watch(plate);

        var commit = CuttingPlanService.Apply([result]);

        Assert.Equal(CuttingCommitStatus.Stale, commit.Status);
        Assert.Same(plate, commit.Plate);
        Assert.False(string.IsNullOrEmpty(commit.Message));
        Assert.True(after.IsCurrent(), after.Difference());
        Assert.Equal((0, 0, 0), events());
    }

    [Fact]
    public void Apply_MalformedLiveProgram_IsStaleInsteadOfThrowing()
    {
        var (_, plate, parts) = FixedPlate();
        var result = CuttingPlanService.Plan(new CuttingPlanRequest(plate));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        parts[1].Program.Codes = null!;
        var events = Watch(plate);

        var commit = CuttingPlanService.Apply([result]);

        Assert.Equal(CuttingCommitStatus.Stale, commit.Status);
        Assert.Equal(parts, plate.Parts);
        Assert.Null(parts[1].Program.Codes);
        Assert.Equal((0, 0, 0), events());
    }

    [Fact]
    public void ProgramContent_ComparesAuthoredKeySpellingExactly()
    {
        Program With(string key)
        {
            var program = new Program();
            program.Codes.Add(new LinearMove(1, 0)
            {
                VariableRefs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [key] = "v" }
            });
            return program;
        }

        Assert.True(ProgramContent.Equal(With("X"), With("X")));
        Assert.False(ProgramContent.Equal(With("X"), With("x")));
    }

    [Fact]
    public void Apply_PartSharedByTwoPlates_IsRefusedWithoutChange()
    {
        var parameters = ExplicitContourTests.Parameters();
        var clean = LeadPathValidationTests.Rectangle(0, 0, 2, 2);
        var prepared = PreparedContours.Capture(clean, parameters);
        var shared = new Part(new Drawing("same", clean), new Vector(5, 5));
        Assert.True(shared.RestoreLeadInProgram(prepared.Emit([prepared.ClosestEntry(0, new Vector(3, 1))]), false));
        var nest = new Nest();
        var regenerated = nest.CreatePlate();
        var fixedPlate = nest.CreatePlate();
        regenerated.Parts.Add(shared);
        fixedPlate.Parts.Add(shared);
        var results = new[]
        {
            CuttingPlanService.Plan(new CuttingPlanRequest(regenerated, confirmedParameters: parameters)),
            CuttingPlanService.Plan(new CuttingPlanRequest(fixedPlate))
        };
        Assert.All(results, r => Assert.Equal(CuttingPlanStatus.Ready, r.Status));
        Assert.True(results[0].ProposedOrder[0].IsRegenerated);
        var program = shared.Program;
        var states = new[] { regenerated, fixedPlate }.Select(p => PlateCuttingState.Capture(p)).ToArray();

        var commit = CuttingPlanService.Apply(results);

        // Installing through one plate would silently replace the other plate's fixed program.
        Assert.Equal(CuttingCommitStatus.InvalidInput, commit.Status);
        Assert.Same(program, shared.Program);
        Assert.All(states, s => Assert.True(s.IsCurrent(), s.Difference()));
    }

    [Fact]
    public void CommitInstaller_IsOnlyReachableThroughTheVerifiedService()
    {
        // The installer validates root references only; owned, verified payloads come from
        // CuttingPlanService.Apply. Public access would accept nested aliases of live programs.
        Assert.False(typeof(CuttingPlanCommit).IsPublic);
        Assert.False(typeof(PlateCuttingPlan).IsPublic);
        Assert.False(typeof(PlannedPartProgram).IsPublic);
        Assert.True(typeof(CuttingPlanService).GetMethod(nameof(CuttingPlanService.Apply),
            [typeof(IEnumerable<CuttingPlanResult>), typeof(CancellationToken)])!.IsPublic);
    }

    [Fact]
    public void Apply_LaterPlateStale_AppliesNothingAnywhere()
    {
        var (_, first, firstParts) = FixedPlate();
        var (_, second, secondParts) = FixedPlate();
        var results = new[]
        {
            CuttingPlanService.Plan(new CuttingPlanRequest(first)),
            CuttingPlanService.Plan(new CuttingPlanRequest(second))
        };
        secondParts[0].LeadInsLocked = true;
        var firstState = PlateCuttingState.Capture(first);
        var events = Watch(first);

        var commit = CuttingPlanService.Apply(results);

        Assert.Equal(CuttingCommitStatus.Stale, commit.Status);
        Assert.Same(second, commit.Plate);
        Assert.Equal(firstParts, first.Parts);
        Assert.True(firstState.IsCurrent(), firstState.Difference());
        Assert.Equal((0, 0, 0), events());
    }

    [Fact]
    public void Apply_InstallFailureOnLaterPlate_RestoresEveryPlateExactlyWithoutPublishing()
    {
        var (_, first, firstParts) = FixedPlate();
        var (_, second, part, parameters) = RegeneratedPlate(Vector.Zero);
        var (_, third, thirdPart, thirdParameters) = RegeneratedPlate(Vector.Zero);
        var results = new[]
        {
            CuttingPlanService.Plan(new CuttingPlanRequest(first)),
            CuttingPlanService.Plan(new CuttingPlanRequest(second, confirmedParameters: parameters)),
            CuttingPlanService.Plan(new CuttingPlanRequest(third, confirmedParameters: thirdParameters))
        };
        Assert.All(results, r => Assert.Equal(CuttingPlanStatus.Ready, r.Status));
        var states = new[] { first, second, third }.Select(p => PlateCuttingState.Capture(p)).ToArray();
        var program = part.Program;
        var bounds = part.BoundingBox;
        var events = new[] { Watch(first), Watch(second), Watch(third) };
        var failure = new InvalidOperationException("injected");
        var installs = 0;

        var commit = CuttingPlanService.Apply(results, default, (plate, _) =>
        {
            installs++;
            if (ReferenceEquals(plate, third)) throw failure;
        });

        Assert.Equal(CuttingCommitStatus.Failed, commit.Status);
        Assert.Same(failure, commit.Error);
        Assert.Equal(2, installs); // The second plate really was installed before the failure.
        Assert.Equal(firstParts, first.Parts);
        Assert.Same(program, part.Program);
        Assert.Same(bounds, part.BoundingBox);
        Assert.Same(parameters, part.CuttingParameters);
        Assert.All(states, s => Assert.True(s.IsCurrent(), s.Difference()));
        Assert.All(events, e => Assert.Equal((0, 0, 0), e()));
        Assert.Same(thirdParameters, thirdPart.CuttingParameters);
    }

    [Fact]
    public void Apply_CancelledBeforeCommit_ChangesNothing()
    {
        var (_, plate, parts) = FixedPlate();
        var result = CuttingPlanService.Plan(new CuttingPlanRequest(plate));
        var state = PlateCuttingState.Capture(plate);
        var events = Watch(plate);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        var commit = CuttingPlanService.Apply([result], cancel.Token);

        Assert.Equal(CuttingCommitStatus.Cancelled, commit.Status);
        Assert.Equal(parts, plate.Parts);
        Assert.True(state.IsCurrent(), state.Difference());
        Assert.Equal((0, 0, 0), events());
    }

    [Fact]
    public void Apply_ObserverFailureAfterPublication_IsAReportedRefreshFailureNotARollback()
    {
        var (_, first, firstParts) = FixedPlate();
        var (_, second, secondParts) = FixedPlate();
        var results = new[]
        {
            CuttingPlanService.Plan(new CuttingPlanRequest(first)),
            CuttingPlanService.Plan(new CuttingPlanRequest(second))
        };
        var failure = new InvalidOperationException("view refresh");
        var seen = new List<string>();
        first.PartsReordered += (_, _) => throw failure;
        first.PartsReordered += (_, _) => seen.Add("first");
        second.PartsReordered += (_, _) => seen.Add("second");

        var commit = CuttingPlanService.Apply(results);

        Assert.Equal(CuttingCommitStatus.Applied, commit.Status);
        Assert.Same(failure, Assert.Single(commit.RefreshErrors));
        Assert.Equal(new[] { "first", "second" }, seen);
        Assert.Equal(new[] { firstParts[1], firstParts[0], firstParts[2] }, first.Parts);
        Assert.Equal(new[] { secondParts[1], secondParts[0], secondParts[2] }, second.Parts);
    }

    [Theory]
    [InlineData("detached")]
    [InlineData("not-ready")]
    [InlineData("duplicate")]
    [InlineData("empty")]
    public void Apply_RefusesResultsThatAreNotApplicablePlatePlans(string fault)
    {
        var (_, plate, parts) = FixedPlate();
        var state = PlateCuttingState.Capture(plate);
        var events = Watch(plate);
        CuttingPlanResult[] results = fault switch
        {
            "detached" => [CuttingPlanService.Plan(new CuttingPlanRequest(parts))],
            "not-ready" => [CuttingPlanService.Plan(new CuttingPlanRequest(plate, preservePartOrder: true))],
            "duplicate" => Enumerable.Repeat(CuttingPlanService.Plan(new CuttingPlanRequest(plate)), 2).ToArray(),
            _ => []
        };
        if (fault == "detached")
            Assert.Equal(CuttingPlanStatus.Ready, results[0].Status);
        if (fault == "not-ready")
            Assert.Equal(CuttingPlanStatus.ConstraintConflict, results[0].Status);

        var commit = CuttingPlanService.Apply(results);

        Assert.Equal(CuttingCommitStatus.InvalidInput, commit.Status);
        Assert.Equal(parts, plate.Parts);
        Assert.True(state.IsCurrent(), state.Difference());
        Assert.Equal((0, 0, 0), events());
    }

    [Fact]
    public void Apply_AllPlatesWithEmptySentinel_KeepsSentinelAndPlateList()
    {
        var nest = new Nest();
        using var manager = new PlateManager(nest);
        var plate = nest.CreatePlate();
        foreach (var part in Fixture())
            plate.Parts.Add(part);
        Assert.Equal(2, nest.Plates.Count);
        var sentinel = nest.Plates[1];
        Assert.Empty(sentinel.Parts);
        var listChanges = 0;
        manager.PlateListChanged += (_, _) => listChanges++;
        var sentinelEvents = Watch(sentinel);
        var results = nest.Plates.ToArray().Select(p => CuttingPlanService.Plan(new CuttingPlanRequest(p))).ToArray();
        Assert.All(results, r => Assert.Equal(CuttingPlanStatus.Ready, r.Status));
        Assert.Empty(results[1].ProposedOrder);

        var commit = CuttingPlanService.Apply(results);

        Assert.Equal(CuttingCommitStatus.Applied, commit.Status);
        Assert.Equal(new[] { plate, sentinel }, nest.Plates);
        Assert.Equal(0, listChanges);
        Assert.Equal((0, 0, 0), sentinelEvents());
        Assert.Equal(3, plate.Parts.Count);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    [InlineData("count")]
    [InlineData("null")]
    public void Reorder_RejectsAnythingButTheSameReferencesWithoutChangeOrEvents(string fault)
    {
        var (_, plate, parts) = FixedPlate();
        var events = Watch(plate);
        Part[] order = fault switch
        {
            "missing" => [parts[0], parts[1], parts[1]],
            "duplicate" => [parts[0], parts[1], parts[2], parts[2]],
            "foreign" => [parts[0], parts[1], Rectangle(0, 0, 1, 1)],
            "count" => [parts[0], parts[1]],
            _ => [parts[0], parts[1], null!]
        };

        Assert.Throws<ArgumentException>(() => plate.Parts.Reorder(order));

        Assert.Equal(parts, plate.Parts);
        Assert.Equal((0, 0, 0), events());
    }

    [Fact]
    public void ProgramContent_DetectsEveryAuthoredFieldAndSharingShape()
    {
        // Guard: a new settable instruction field must be added to ProgramContent and here.
        var expected = new Dictionary<Type, string[]>
        {
            [typeof(RapidMove)] = ["EndPoint", "Feedrate", "Suppressed", "UseExactStop", "VariableRefs"],
            [typeof(LinearMove)] = ["EndPoint", "Feedrate", "Layer", "Suppressed", "UseExactStop", "VariableRefs"],
            [typeof(ArcMove)] = ["CenterPoint", "EndPoint", "Feedrate", "Layer", "Rotation", "Suppressed",
                "UseExactStop", "VariableRefs"],
            [typeof(SubProgramCall)] = ["Id", "Offset", "Program", "Rotation"],
            [typeof(Comment)] = ["Value"],
            [typeof(Feedrate)] = ["Value", "VariableRef"],
            [typeof(Kerf)] = ["Value"],
            [typeof(Program)] = ["Mode"]
        };
        foreach (var (type, names) in expected)
            Assert.Equal(names, type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod?.IsPublic == true && p.GetIndexParameters().Length == 0)
                .Select(p => p.Name).Order());

        var mutations = new Action<Program>[]
        {
            p => ((Motion)p.Codes[0]).EndPoint = Nudge(((Motion)p.Codes[0]).EndPoint),
            p => ((Motion)p.Codes[1]).UseExactStop = true,
            p => ((Motion)p.Codes[1]).Feedrate = 3,
            p => ((Motion)p.Codes[1]).Suppressed = true,
            p => ((Motion)p.Codes[1]).VariableRefs!["x"] = "other",
            p => ((Motion)p.Codes[1]).VariableRefs = null,
            p => ((LinearMove)p.Codes[1]).Layer = LayerType.Display,
            p => ((ArcMove)p.Codes[2]).Layer = LayerType.Display,
            p => ((ArcMove)p.Codes[2]).Rotation = RotationType.CW,
            p => ((ArcMove)p.Codes[2]).CenterPoint = Nudge(((ArcMove)p.Codes[2]).CenterPoint),
            p => ((SubProgramCall)p.Codes[3]).Id = 9,
            p => ((SubProgramCall)p.Codes[3]).Offset = Nudge(((SubProgramCall)p.Codes[3]).Offset),
            p => ((SubProgramCall)p.Codes[3]).Rotation = 1,
            p => ((SubProgramCall)p.Codes[3]).Program.Codes.Add(new Comment("hole")),
            p => ((Comment)p.Codes[5]).Value = "other",
            p => ((Feedrate)p.Codes[6]).Value = 2,
            p => ((Feedrate)p.Codes[6]).VariableRef = "f",
            p => ((Kerf)p.Codes[7]).Value = KerfType.Right,
            p => p.Mode = Mode.Incremental,
            p => p.Rotate(1e-9),
            p => p.Variables["v"] = new VariableDefinition("v", "2", 2),
            p => p.SubPrograms[5] = new Program(),
            // Two calls sharing one hole versus two equal but distinct holes.
            p => ((SubProgramCall)p.Codes[4]).BindProgram(OwnedProgramCopy.Copy(((SubProgramCall)p.Codes[4]).Program))
        };
        var original = Rich();
        Assert.True(ProgramContent.Equal(original, OwnedProgramCopy.Copy(original)));
        for (var i = 0; i < mutations.Length; i++)
        {
            var changed = OwnedProgramCopy.Copy(original);
            mutations[i](changed);
            Assert.False(ProgramContent.Equal(original, changed), $"Mutation {i} was not detected.");
        }
    }

    private static Vector Nudge(Vector v) =>
        new(BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(v.X) + 1), v.Y);

    private static Program Rich()
    {
        var hole = new Program();
        hole.Codes.Add(new LinearMove(1, 0) { Layer = LayerType.Cut });
        var program = new Program();
        program.Variables["v"] = new VariableDefinition("v", "1", 1);
        program.Codes.Add(new RapidMove(1, 1));
        program.Codes.Add(new LinearMove(2, 1)
        {
            Layer = LayerType.Cut,
            VariableRefs = new Dictionary<string, string> { ["x"] = "v" }
        });
        program.Codes.Add(new ArcMove(new Vector(3, 2), new Vector(2, 2), RotationType.CCW) { Layer = LayerType.Cut });
        program.Codes.Add(new SubProgramCall(hole, 0) { Id = 1, Offset = new Vector(5, 5) });
        program.Codes.Add(new SubProgramCall(hole, 0) { Id = 1, Offset = new Vector(7, 5) });
        program.Codes.Add(new Comment("note"));
        program.Codes.Add(new Feedrate(1));
        program.Codes.Add(new Kerf(KerfType.Left));
        program.SubPrograms[1] = hole;
        return program;
    }

    private static Func<(int Added, int Removed, int Reordered)> Watch(Plate plate)
    {
        var added = 0;
        var removed = 0;
        var reordered = 0;
        plate.PartAdded += (_, _) => added++;
        plate.PartRemoved += (_, _) => removed++;
        plate.PartsReordered += (_, _) => reordered++;
        return () => (added, removed, reordered);
    }

    private static (Nest, Plate, Part[]) FixedPlate()
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        var parts = Fixture();
        foreach (var part in parts)
            plate.Parts.Add(part);
        return (nest, plate, parts);
    }

    private static (Nest, Plate, Part, CuttingParameters) RegeneratedPlate(Vector location)
    {
        var clean = PreparedContourTests.Holes();
        var parameters = ExplicitContourTests.Parameters();
        var prepared = PreparedContours.Capture(clean, parameters);
        var original = prepared.Emit(
        [
            prepared.Entry(0, 0, new Vector(2, 3)),
            prepared.Entry(1, 0, new Vector(8, 3)),
            prepared.ClosestEntry(2, new Vector(-2, 3))
        ]);
        var part = new Part(new Drawing("same", clean), location);
        Assert.True(part.RestoreLeadInProgram(original, false));
        part.CuttingParameters = parameters;
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Parts.Add(part);
        return (nest, plate, part, parameters);
    }

    // Slice 1 fixture: A,B,C in this order crosses completed A; B,A,C does not.
    private static Part[] Fixture() =>
    [
        Rectangle(4, 0, 4, 4),
        Rectangle(0, 1, 2, 2),
        Rectangle(10, 1, 2, 2)
    ];

    private static Part Rectangle(double x, double y, double width, double height)
    {
        var clean = new Program();
        clean.MoveTo(width, height / 2);
        Contour(clean, width, height);
        var part = new Part(new Drawing("same", clean), new Vector(x, y));
        var placed = new Program();
        placed.MoveTo(width + 0.25, height / 2);
        placed.Codes.Add(new LinearMove(width, height / 2) { Layer = LayerType.Leadin });
        Contour(placed, width, height);
        placed.Codes.Add(new LinearMove(width + 0.25, height / 2) { Layer = LayerType.Leadout });
        Assert.True(part.RestoreLeadInProgram(placed, false));
        part.CuttingParameters = new CuttingParameters();
        return part;
    }

    private static void Contour(Program program, double width, double height)
    {
        program.LineTo(width, 0);
        program.LineTo(0, 0);
        program.LineTo(0, height);
        program.LineTo(width, height);
        program.LineTo(width, height / 2);
    }
}
