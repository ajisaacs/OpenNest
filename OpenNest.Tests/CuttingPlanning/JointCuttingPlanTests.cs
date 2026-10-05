using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class JointCuttingPlanTests
{
    [Fact]
    public void JointSearch_RepairsActualCompletedHoleCrossing_WithExactReplayAndNoMutation()
    {
        var (part, parameters) = Crossing();
        var unchanged = Unchanged(part, parameters);
        Assert.Contains(new ReleasedContourState().Check(Read(part.Program, part.Location), Vector.Zero, 1),
            f => f.Kind == PostVerificationKind.RapidCrossing);
        Assert.Equal(CuttingPlanStatus.ConstraintConflict,
            CuttingPlanService.Plan(new CuttingPlanRequest([part])).Status);
        var result = CuttingPlanService.Plan(new CuttingPlanRequest([part], confirmedParameters: parameters));
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.True(result.IndependentlyReplayed);
        var proposal = Assert.Single(result.ProposedOrder);
        Assert.True(proposal.IsRegenerated);
        Assert.Equal(new[] { 0, 1, 2 }, proposal.ContourChoices.Select(c => c.ContourOrdinal).Order());
        Assert.Equal(2, proposal.ContourChoices[^1].ContourOrdinal);
        Assert.NotEqual(ExplicitContourTests.Fingerprint(part.Program), ExplicitContourTests.Fingerprint(proposal.CopyProgram()));
        var actual = Read(proposal.CopyProgram(), proposal.Location);
        Assert.Empty(new ReleasedContourState().Check(actual, Vector.Zero, 1));
        var material = LeadMaterialSnapshot.Capture(part.BaseDrawing.Program, part.Location);
        var leads = LeadPathValidator.Check(actual, material, []);
        Assert.True(leads.IsComplete && leads.IsClear, leads.Reason);
        var nest = new Nest();
        var plate = nest.CreatePlate();
        var copy = new Part(new Drawing("replay", (Program)part.BaseDrawing.Program.Clone()), proposal.Location);
        Assert.True(copy.RestoreLeadInProgram(proposal.CopyProgram(), false));
        plate.Parts.Add(copy);
        var report = PostVerificationAnalyzer.Analyze(nest, Vector.Zero);
        Assert.Empty(report.Findings);
        var detached = proposal.CopyProgram(); detached.Codes.Clear(); detached.SubPrograms.Clear();
        Assert.NotEmpty(proposal.CopyProgram().Codes);
        unchanged();
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("ineligible")]
    [InlineData("no-valid-entry")]
    [InlineData("unsupported")]
    public void JointSearch_RefusesWithoutInstallingFallbackOrMutating(string fault)
    {
        var (part, parameters) = Crossing();
        Part[]? eligible = null;
        var expected = CuttingPlanStatus.ConstraintConflict;
        if (fault == "locked") part.LeadInsLocked = true;
        if (fault == "ineligible") eligible = [];
        if (fault == "no-valid-entry")
        {
            parameters.ArcCircleLeadIn = new NoLeadIn();
            parameters.InternalLeadIn = parameters.ArcCircleLeadIn;
            expected = CuttingPlanStatus.NoSolutionWithinBudget;
        }
        if (fault == "unsupported")
        {
            part.BaseDrawing.Program.Codes.AddRange(LeadPathValidationTests.Rectangle(30, 30, 40, 40).Codes);
            expected = CuttingPlanStatus.UnsupportedGeometry;
        }
        var unchanged = Unchanged(part, parameters);
        var result = CuttingPlanService.Plan(new CuttingPlanRequest([part], confirmedParameters: parameters, eligibleParts: eligible));
        Assert.Equal(expected, result.Status);
        Assert.Empty(result.ProposedOrder);
        Assert.False(result.IndependentlyReplayed);
        unchanged();
    }

    [Fact]
    public void JointSearch_BacktracksInternalEntryWhenLaterWholePartFails()
    {
        var parameters = ExplicitContourTests.Parameters();
        var first = SimplePart(Vector.Zero, parameters);
        var second = SimplePart(new Vector(20, 0), parameters);
        second.LeadInsLocked = true;
        var start = new Vector(-2, 5);
        var solo = CuttingPlanService.Plan(new CuttingPlanRequest([first], start, confirmedParameters: parameters));
        Assert.Equal(CuttingPlanStatus.Ready, solo.Status);
        var fixedLater = CuttingPlanService.Capture(new CuttingPlanRequest([second])).Placements[0];
        var state = new ReleasedContourState();
        Assert.Empty(state.Check(solo.ProposedOrder[0].Execution, start, 1));
        Assert.Contains(state.Check(fixedLater.Execution, solo.ProposedOrder[0].Execution.DeparturePoint, 2),
            f => f.Kind == PostVerificationKind.RapidCrossing);
        var joint = CuttingPlanService.Plan(new CuttingPlanRequest([first, second], start,
            confirmedParameters: parameters, preservePartOrder: true));
        Assert.True(joint.Status == CuttingPlanStatus.Ready, Describe(joint));
        Assert.Equal(new[] { first, second }, joint.ProposedOrder.Select(p => p.SourcePart));
        Assert.NotEqual(solo.ProposedOrder[0].ContourChoices[0].Point, joint.ProposedOrder[0].ContourChoices[0].Point);
        Assert.True(joint.IndependentlyReplayed);
    }

    [Fact]
    public void Snapshot_CallerMutationAndDuplicateNames_DoNotChangeDeterministicOwnedProposals()
    {
        var (first, parameters) = Crossing();
        var (second, _) = Crossing();
        second.Location = new Vector(30, 0);
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([first, second], confirmedParameters: parameters));
        var baseline = CuttingPlanService.Plan(snapshot);
        Assert.True(baseline.Status == CuttingPlanStatus.Ready, Describe(baseline));
        var fingerprints = baseline.ProposedOrder.Select(p => ExplicitContourTests.Fingerprint(p.CopyProgram())).ToArray();
        foreach (var part in new[] { first, second })
        {
            foreach (var call in part.Program.Codes.OfType<SubProgramCall>()) call.Program.Codes.Clear();
            part.Program.Codes.Clear(); part.BaseDrawing.Program.Codes.Clear();
            part.Location = new Vector(999, 999); part.LeadInsLocked = true;
            part.CuttingParameters = new CuttingParameters();
        }
        ((LineLeadIn)parameters.ExternalLeadIn).Length = 900;
        parameters.TabsEnabled = true;
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var result = CuttingPlanService.Plan(snapshot);
            Assert.Equal(CuttingPlanStatus.Ready, result.Status);
            Assert.Equal(baseline.Expansions, result.Expansions);
            Assert.Equal(baseline.RapidDistance, result.RapidDistance);
            Assert.Equal(baseline.ProposedOrder.Select(p => p.SourcePart), result.ProposedOrder.Select(p => p.SourcePart));
            Assert.Equal(fingerprints, result.ProposedOrder.Select(p => ExplicitContourTests.Fingerprint(p.CopyProgram())));
        }
        Assert.Equal(new[] { first, second }, baseline.ProposedOrder.Select(p => p.SourcePart));
    }

    [Fact]
    public void BudgetOneAndCancellationDuringExpansion_HaveExactCountsNoFallbackNoMutation()
    {
        var (part, parameters) = Crossing();
        var unchanged = Unchanged(part, parameters);
        var observed = 0;
        var budget = CuttingPlanService.Plan(new CuttingPlanRequest([part], expansionBudget: 1,
            confirmedParameters: parameters)
        { ExpansionObserver = n => observed = n });
        Assert.Equal(CuttingPlanStatus.NoSolutionWithinBudget, budget.Status);
        Assert.Equal(1, budget.Expansions); Assert.Equal(1, observed); Assert.Empty(budget.ProposedOrder);
        using var cancellation = new CancellationTokenSource();
        var cancelled = CuttingPlanService.Plan(new CuttingPlanRequest([part], confirmedParameters: parameters)
        {
            ExpansionObserver = n => { if (n == 3) cancellation.Cancel(); }
        }, cancellation.Token);
        Assert.Equal(CuttingPlanStatus.Cancelled, cancelled.Status);
        Assert.Equal(3, cancelled.Expansions); Assert.Empty(cancelled.ProposedOrder);
        unchanged();
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("duplicate")]
    [InlineData("without-settings")]
    [InlineData("zero-cap")]
    public void EligibilityAndEntryBounds_InvalidInputIsNotIgnored(string fault)
    {
        var (part, parameters) = Crossing();
        var other = SimplePart(new Vector(30, 0), parameters);
        var request = new CuttingPlanRequest([part], confirmedParameters: fault == "without-settings" ? null : parameters,
            eligibleParts: fault == "foreign" ? [other] : fault == "duplicate" ? [part, part] : [part],
            maxEntries: fault == "zero-cap" ? 0 : 16);
        Assert.Equal(CuttingPlanStatus.InvalidInput, CuttingPlanService.Plan(request).Status);
    }

    [Theory]
    [InlineData(0.0, 1.5707963267948966)]
    [InlineData(0.37, 0.91)]
    public void RotatedCapture_UsesDeltaFromAlreadyRotatedBase_AndLocationOnce(double baseAngle, double delta)
    {
        var parameters = ExplicitContourTests.Parameters();
        var clean = PreparedContourTests.Holes(); clean.Rotate(baseAngle);
        var part = new Part(new Drawing("rotated", clean)); part.Rotate(delta);
        part.Location = new Vector(30, 40);
        var expected = (Program)clean.Clone(); expected.Rotate(delta);
        var target = LeadMaterialSnapshot.Capture(expected, part.Location);
        var result = CuttingPlanService.Plan(new CuttingPlanRequest([part], new Vector(20, 30), confirmedParameters: parameters));
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        var proposal = Assert.Single(result.ProposedOrder);
        Assert.Equal(part.Rotation, proposal.Rotation);
        Assert.Equal(part.Location, proposal.Location);
        var lead = LeadPathValidator.Check(Read(proposal.CopyProgram(), part.Location), target, []);
        Assert.True(lead.IsComplete && lead.IsClear, lead.Reason);
    }

    [Fact]
    public void GeneratedLeadoutDeparture_IsUsedForArrivalAndReplayDistance()
    {
        var parameters = ExplicitContourTests.Parameters();
        parameters.ExternalLeadOut = new LineLeadOut { Length = 0.2, ApproachAngle = 45 };
        var parts = new[] { SimplePart(Vector.Zero, parameters), SimplePart(new Vector(20, 0), parameters) };
        var start = new Vector(-2, 5);
        var result = CuttingPlanService.Plan(new CuttingPlanRequest(parts, start,
            confirmedParameters: parameters, preservePartOrder: true));
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        var distance = 0.0; var arrival = start;
        foreach (var p in result.ProposedOrder)
        {
            var actual = Read(p.CopyProgram(), p.Location);
            Assert.Equal(LayerType.Leadout, actual.Motions[^1].Layer);
            Assert.NotEqual(actual.Motions[^1].Start, actual.DeparturePoint);
            Assert.Equal(actual.DeparturePoint, p.Execution.DeparturePoint);
            distance += actual.RapidDistanceFrom(arrival); arrival = actual.DeparturePoint;
        }
        Assert.Equal(distance, result.RapidDistance);
    }

    [Theory]
    [InlineData("unsafe")]
    [InlineData("duplicate-choice")]
    [InlineData("omitted-contour")]
    [InlineData("duplicate-placement")]
    [InlineData("omitted-placement")]
    [InlineData("wrong-pose")]
    public void ExactReplay_RejectsUnsafeOrIncompleteSelectedPrograms(string fault)
    {
        var (part, parameters) = Crossing();
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part], confirmedParameters: parameters));
        var ready = CuttingPlanService.Plan(snapshot);
        Assert.Equal(CuttingPlanStatus.Ready, ready.Status);
        var proposal = ready.ProposedOrder[0];
        var source = snapshot.Placements[0];
        IReadOnlyList<FixedProgramPlacement> order = [proposal];
        if (fault == "unsafe") order = [source.Propose((Program)part.Program.Clone(), Read(part.Program, part.Location), proposal.ContourChoices)];
        if (fault == "duplicate-choice") order = [source.Propose(proposal.CopyProgram(), proposal.Execution,
            [proposal.ContourChoices[0], proposal.ContourChoices[0], proposal.ContourChoices[2]])];
        if (fault == "omitted-contour")
        {
            var prefix = source.Prepared.EmitPrefix(proposal.ContourChoices.Take(2).ToArray());
            order = [source.Propose(prefix, Read(prefix, source.Location), proposal.ContourChoices)];
        }
        if (fault == "duplicate-placement") order = [proposal, proposal];
        if (fault == "omitted-placement") order = [];
        if (fault == "wrong-pose") order = [new FixedProgramPlacement(part, 0, new Vector(99, 99), source.Rotation,
            false, proposal.Execution, proposal.CopyProgram(), source.Prepared, source.Material, proposal.ContourChoices)];
        var replay = CuttingPlanService.ReplayPrograms(snapshot, order, 0, default);
        Assert.Equal(fault == "unsafe" ? CuttingPlanStatus.ConstraintConflict : CuttingPlanStatus.InvalidInput, replay.Status);
        Assert.Empty(replay.ProposedOrder); Assert.False(replay.IndependentlyReplayed);
    }

    [Fact]
    public void FixedLeads_SearchAndReplayRejectNativeTargetMaterialCrossing()
    {
        var parameters = ExplicitContourTests.Parameters();
        var clean = LeadPathValidationTests.Rectangle(0, 0, 10, 10);
        var part = new Part(new Drawing("fixed", clean));
        var unsafeProgram = new Program(); unsafeProgram.MoveTo(5, 5);
        unsafeProgram.Codes.Add(new LinearMove(0, 5) { Layer = LayerType.Leadin });
        unsafeProgram.LineTo(0, 10); unsafeProgram.LineTo(10, 10); unsafeProgram.LineTo(10, 0);
        unsafeProgram.LineTo(0, 0); unsafeProgram.LineTo(0, 5);
        Assert.True(part.RestoreLeadInProgram(unsafeProgram, true));
        Assert.Empty(new ReleasedContourState().Check(Read(unsafeProgram, Vector.Zero), Vector.Zero, 1));
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part], confirmedParameters: parameters));
        var search = JointCuttingPlanSearch.Run(snapshot, default);
        Assert.Equal(CuttingPlanStatus.ConstraintConflict, search.Status);
        Assert.Empty(search.Order);
        var replay = CuttingPlanService.ReplayPrograms(snapshot, snapshot.Placements, 0, default);
        Assert.Equal(CuttingPlanStatus.ConstraintConflict, replay.Status);
        Assert.Contains(replay.Findings, f => f.Message.Contains("target material"));
    }

    [Fact]
    public void TabbedArcCandidate_UncertifiedTabGap_IsRefusedWithoutRepairOrException()
    {
        // The arc lead-out is now well formed; the open tab gap is still not certified.
        var parameters = ExplicitContourTests.Parameters();
        parameters.TabsEnabled = true; parameters.TabConfig = new NormalTab { Size = 0.2 };
        parameters.ExternalLeadOut = new ArcLeadOut { Radius = 0.2 };
        var part = new Part(new Drawing("tabbed", LeadPathValidationTests.Rectangle(0, 0, 10, 10)));
        var result = CuttingPlanService.Plan(new CuttingPlanRequest([part], new Vector(-2, 5), confirmedParameters: parameters));
        Assert.Equal(CuttingPlanStatus.NoSolutionWithinBudget, result.Status);
        Assert.Empty(result.ProposedOrder); Assert.True(result.Expansions > 0);
        Assert.Contains(result.Findings, f => f.Kind == PostVerificationKind.Incomplete);
        Assert.Contains(result.Findings, f => f.Message.Contains("retention gap"));
    }

    [Fact]
    public void FixedLead_SearchChecksOtherUncutHoleAwareMaterial_NotOnlyReleasedContours()
    {
        var parameters = ExplicitContourTests.Parameters();
        var first = SimplePart(Vector.Zero, parameters); first.LeadInsLocked = true;
        var clean = LeadPathValidationTests.Rectangle(-0.3, 4.5, -0.1, 5.5);
        var obstacle = new Part(new Drawing("uncut", clean));
        var prepared = PreparedContours.Capture(clean, parameters);
        Assert.True(obstacle.RestoreLeadInProgram(prepared.Emit([prepared.ClosestEntry(0, new Vector(-1, 5))]), true));
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([first, obstacle],
            new Vector(-2, 5), confirmedParameters: parameters, preservePartOrder: true));
        var direct = LeadPathValidator.Check(snapshot.Placements[0].Execution, snapshot.Placements[0].Material,
            snapshot.Placements.Select(p => p.Material).ToArray());
        Assert.True(direct.IsComplete); Assert.False(direct.IsClear);
        Assert.Contains("another placed material", direct.Reason);
        var search = JointCuttingPlanSearch.Run(snapshot, default);
        Assert.Equal(CuttingPlanStatus.ConstraintConflict, search.Status);
        Assert.Contains(search.Findings, f => f.Message.Contains("another placed material"));
    }

    [Fact]
    public void UnsupportedMaterialOfLockedIneligiblePlacement_BlocksCompleteRegeneration()
    {
        var (part, parameters) = Crossing();
        var locked = SimplePart(new Vector(30, 0), parameters); locked.LeadInsLocked = true;
        locked.BaseDrawing.Program.Codes.RemoveAt(locked.BaseDrawing.Program.Codes.Count - 1);
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part, locked], confirmedParameters: parameters,
            eligibleParts: [part]));
        var result = CuttingPlanService.Plan(snapshot);
        Assert.Equal(CuttingPlanStatus.UnsupportedGeometry, result.Status);
        Assert.Empty(result.ProposedOrder);
        Assert.Contains(result.Findings, f => ReferenceEquals(f.SourcePart, locked) && f.SourceOrdinal == 1);
    }

    [Fact]
    public void JointSearch_SelectsWholePartOrderAndCanPreserveCallerOrder()
    {
        var (first, parameters) = Crossing(); first.Location = new Vector(30, 0);
        var (second, _) = Crossing();
        var free = CuttingPlanService.Plan(new CuttingPlanRequest([first, second], confirmedParameters: parameters));
        var preserved = CuttingPlanService.Plan(new CuttingPlanRequest([first, second], confirmedParameters: parameters,
            preservePartOrder: true));
        Assert.True(free.Status == CuttingPlanStatus.Ready, Describe(free));
        Assert.True(preserved.Status == CuttingPlanStatus.Ready, Describe(preserved));
        Assert.Equal(new[] { second, first }, free.ProposedOrder.Select(p => p.SourcePart));
        Assert.Equal(new[] { first, second }, preserved.ProposedOrder.Select(p => p.SourcePart));
    }

    [Fact]
    public void FixedProgramCopy_PreservesSignedSubcallsCommentsAndVariables_WithoutMutableAliases()
    {
        var (part, _) = Crossing();
        part.Program.Codes.Insert(0, new Comment("fixed text"));
        part.Program.Variables["value"] = new VariableDefinition("value", "2+3", 5);
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part]));
        var placement = snapshot.Placements[0];
        var copy = placement.CopyProgram();
        Assert.Equal(part.Program.ToString(), copy.ToString());
        Assert.Equal(part.Program.Variables["value"].Expression, copy.Variables["value"].Expression);
        Assert.Equal(part.Program.Codes.OfType<SubProgramCall>().Select(c => (c.Id, c.Offset, c.Rotation)),
            copy.Codes.OfType<SubProgramCall>().Select(c => (c.Id, c.Offset, c.Rotation)));
        Assert.Equal(part.Program.Codes.OfType<Comment>().Select(c => c.ToString()), copy.Codes.OfType<Comment>().Select(c => c.ToString()));
        foreach (var call in copy.Codes.OfType<SubProgramCall>()) call.Program.Codes.Clear();
        copy.Codes.Clear(); copy.Variables.Clear();
        Assert.NotEmpty(placement.CopyProgram().Codes);
        Assert.NotEmpty(part.Program.Codes);
    }

    [Fact]
    public void Regeneration_CompletesOriginallyOmittedHoles_AndPrefixesKeepScribesOnce()
    {
        var parameters = ExplicitContourTests.Parameters();
        var clean = PreparedContourTests.Holes(); clean.MoveTo(-2, -2);
        clean.Codes.Add(new LinearMove(-1, -1) { Layer = LayerType.Scribe });
        var part = new Part(new Drawing("incomplete old program", clean));
        var original = SimplePart(Vector.Zero, parameters).Program;
        Assert.True(part.RestoreLeadInProgram((Program)original.Clone(), false));
        part.CuttingParameters = parameters;
        var unchanged = Unchanged(part, parameters);
        var result = CuttingPlanService.Plan(new CuttingPlanRequest([part], confirmedParameters: parameters));
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        var proposal = Assert.Single(result.ProposedOrder);
        Assert.Equal(new[] { 0, 1, 2 }, proposal.ContourChoices.Select(c => c.ContourOrdinal).Order());
        var actual = Read(proposal.CopyProgram(), Vector.Zero);
        Assert.Single(actual.Motions.Where(m => m.Layer == LayerType.Scribe));
        Assert.Empty(new ReleasedContourState().Check(actual, Vector.Zero, 1));
        unchanged();
    }

    [Fact]
    public void FixedProgramCapture_PreservesInactiveMotionlessRegisteredSubprogram()
    {
        var parameters = ExplicitContourTests.Parameters();
        var part = SimplePart(Vector.Zero, parameters);
        part.Program.SubPrograms[-27] = new Program();
        var result = CuttingPlanService.Plan(new CuttingPlanRequest([part]));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.Empty(result.ProposedOrder[0].CopyProgram().SubPrograms[-27].Codes);
    }

    private static Part SimplePart(Vector location, CuttingParameters parameters)
    {
        var clean = LeadPathValidationTests.Rectangle(0, 0, 10, 10);
        var part = new Part(new Drawing("same", clean), location);
        var prepared = PreparedContours.Capture(clean, parameters);
        var emitted = prepared.Emit([prepared.ClosestEntry(0, new Vector(-2, 5))]);
        Assert.True(part.RestoreLeadInProgram(emitted, false)); part.CuttingParameters = parameters;
        return part;
    }

    private static (Part, CuttingParameters) Crossing()
    {
        var clean = PreparedContourTests.Holes();
        var parameters = ExplicitContourTests.Parameters();
        var prepared = PreparedContours.Capture(clean, parameters);
        var choices = new[]
        {
            prepared.Entry(0, 0, new Vector(2, 3)),
            prepared.Entry(1, 0, new Vector(8, 3)),
            prepared.ClosestEntry(2, new Vector(-2, 3))
        };
        var original = prepared.Emit(choices);
        var part = new Part(new Drawing("same", clean));
        Assert.True(part.RestoreLeadInProgram(original, false));
        part.CuttingParameters = parameters;
        return (part, parameters);
    }

    private static Action Unchanged(Part part, CuttingParameters parameters)
    {
        var program = part.Program;
        var clean = part.BaseDrawing.Program;
        var fingerprint = ExplicitContourTests.Fingerprint(program);
        var cleanText = clean.ToString();
        var codes = program.Codes.ToArray();
        var subs = program.Codes.OfType<SubProgramCall>()
            .Select(c => (c, c.Program, text: c.Program.ToString(), c.Id, c.Offset, c.Rotation)).ToArray();
        var location = part.Location;
        var rotation = part.Rotation;
        var locked = part.LeadInsLocked;
        var manual = part.HasManualLeadIns;
        var bounds = part.BoundingBox;
        var quantity = part.BaseDrawing.Quantity.Nested;
        var length = ((LineLeadIn)parameters.ExternalLeadIn).Length;
        return () =>
        {
            Assert.Same(program, part.Program); Assert.Same(clean, part.BaseDrawing.Program);
            Assert.Equal(fingerprint, ExplicitContourTests.Fingerprint(program)); Assert.Equal(cleanText, clean.ToString());
            Assert.Equal(codes, program.Codes); Assert.Equal(location, part.Location); Assert.Equal(rotation, part.Rotation);
            Assert.Equal(locked, part.LeadInsLocked); Assert.Equal(manual, part.HasManualLeadIns);
            Assert.Same(bounds, part.BoundingBox); Assert.Same(parameters, part.CuttingParameters);
            Assert.Equal(quantity, part.BaseDrawing.Quantity.Nested);
            Assert.Equal(length, ((LineLeadIn)parameters.ExternalLeadIn).Length);
            foreach (var sub in subs)
            {
                Assert.Same(sub.Program, sub.c.Program); Assert.Equal(sub.text, sub.c.Program.ToString());
                Assert.Equal(sub.Id, sub.c.Id); Assert.Equal(sub.Offset, sub.c.Offset); Assert.Equal(sub.Rotation, sub.c.Rotation);
            }
        };
    }

    private static OwnedExecution Read(Program p, Vector location) => ExecutionMotionReader.Read(p, location, null, default);
    private static string Describe(CuttingPlanResult r) => $"{r.Status}, expanded {r.Expansions}: " + string.Join("; ", r.Findings.Select(f => f.Message));
}
