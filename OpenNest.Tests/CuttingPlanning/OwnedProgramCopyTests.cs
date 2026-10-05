using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class OwnedProgramCopyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FixedPayload_PreservesEveryAuthoredFieldAndGraphAlias(bool confirmed, bool locked)
    {
        var parameters = ExplicitContourTests.Parameters();
        var clean = LeadPathValidationTests.Rectangle(0, 0, 10, 10);
        var prepared = PreparedContours.Capture(clean, parameters);
        var emitted = prepared.Emit([prepared.ClosestEntry(0, new Vector(-2, 5))]);
        var part = new Part(new Drawing("metadata", clean));
        Assert.True(part.RestoreLeadInProgram(emitted, locked));
        part.Rotate(0.2);
        AddMetadata(part.Program);
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part], new Vector(-2, 5),
            confirmedParameters: confirmed ? parameters : null, eligibleParts: confirmed ? [] : null));
        var captured = Assert.Single(snapshot.Placements);
        AssertGraph(part.Program, captured.CopyProgram());
        var result = CuttingPlanService.Plan(snapshot);
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        var proposal = Assert.Single(result.ProposedOrder);
        AssertGraph(part.Program, proposal.CopyProgram());
        var detached = proposal.CopyProgram();
        var expectedFeed = ((Motion)part.Program.Codes.First(c => c is Motion)).Feedrate;
        ((Motion)detached.Codes.First(c => c is Motion)).Feedrate = 999;
        detached.SubPrograms[-40].Codes.Clear();
        detached.Variables.Clear();
        AssertGraph(part.Program, proposal.CopyProgram());
        part.Program.Codes.Clear();
        part.Program.SubPrograms[-40].Codes.Clear();
        part.Program.Variables.Clear();
        var retained = proposal.CopyProgram();
        Assert.Equal(expectedFeed, ((Motion)retained.Codes.First(c => c is Motion)).Feedrate);
        Assert.NotEmpty(retained.SubPrograms[-40].Codes);
        Assert.NotEmpty(retained.Variables);
    }

    [Fact]
    public void Propose_OwnsLosslessStorageRatherThanRetainingCallerProgram()
    {
        var part = new Part(new Drawing("proposal", LeadPathValidationTests.Rectangle(0, 0, 10, 10)));
        var captured = Assert.Single(CuttingPlanService.Capture(new CuttingPlanRequest([part])).Placements);
        var authored = captured.CopyProgram();
        AddMetadata(authored);
        var proposed = captured.Propose(authored, ExecutionMotionReader.ReadSupported(authored, Vector.Zero, null), []);
        AssertGraph(authored, proposed.CopyProgram());
        authored.Codes.Clear(); authored.SubPrograms[-40].Codes.Clear(); authored.Variables.Clear();
        var retained = proposed.CopyProgram();
        Assert.NotEmpty(retained.Codes);
        Assert.NotEmpty(retained.SubPrograms[-40].Codes);
        Assert.NotEmpty(retained.Variables);
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("depth")]
    [InlineData("cached-depth")]
    [InlineData("expansion")]
    public void OwnedCopy_RefusesUnsafeCloneTraversalBeforeInvokingClone(string kind)
    {
        var root = new Program();
        if (kind == "cycle") root.SubPrograms[-1] = root;
        if (kind is "depth" or "cached-depth")
        {
            var child = new Program();
            if (kind == "cached-depth") root.SubPrograms[-2] = child;
            var parent = child;
            for (var i = 0; i < 64; i++)
            {
                var next = new Program(); next.SubPrograms[-1] = parent; parent = next;
            }
            root.SubPrograms[-1] = parent;
        }
        if (kind == "expansion")
        {
            var child = new Program();
            for (var i = 0; i < 20; i++)
            {
                var left = new Program(); left.SubPrograms[-1] = child;
                var right = new Program(); right.SubPrograms[-1] = child;
                var parent = new Program(); parent.SubPrograms[-1] = left; parent.SubPrograms[-2] = right;
                child = parent;
            }
            root.SubPrograms[-1] = child;
        }
        Assert.Throws<ArgumentException>(() => OwnedProgramCopy.Copy(root));
    }

    [Fact]
    public void OwnedCopy_HonorsCancellationBeforeClone()
    {
        Assert.Throws<OperationCanceledException>(() => OwnedProgramCopy.Copy(new Program(), new CancellationToken(true)));
    }

    private static void AddMetadata(Program root)
    {
        var leaf = new Program();
        leaf.MoveTo(-1, 0);
        leaf.Codes.Add(new LinearMove(-2, 0) { Layer = LayerType.Scribe });
        leaf.Codes.Add(new ArcMove(-1, 1, -1, 0, RotationType.CW) { Layer = LayerType.Scribe });
        leaf.Rotate(0.37);
        leaf.Mode = Mode.Incremental;
        var degrees = 0.37 * 180 / System.Math.PI;
        var middle = new Program();
        middle.Codes.Add(new SubProgramCall(leaf, degrees) { Id = -7, Offset = new Vector(-1, 0) });
        middle.SubPrograms[-7] = leaf;
        root.Codes.Add(new SubProgramCall(middle, 0) { Id = -9, Offset = new Vector(-5, -5) });
        root.Codes.Add(new SubProgramCall(leaf, degrees) { Id = -7, Offset = new Vector(-10, -10) });
        root.SubPrograms[-9] = middle;
        root.SubPrograms[-7] = leaf;
        var inactive = new Program(Mode.Incremental);
        inactive.Codes.Add(new LinearMove(4, 5) { Suppressed = true, Layer = LayerType.Display });
        inactive.SubPrograms[-7] = leaf;
        root.SubPrograms[-40] = inactive;
        root.SubPrograms[-41] = new Program(); // Accepted motionless inactive registration.
        foreach (var p in new[] { root, middle, leaf, inactive })
        {
            p.Codes.Insert(0, new Comment("literal, : #value"));
            p.Codes.Insert(1, new Feedrate(123.5) { VariableRef = "speed" });
            p.Codes.Insert(2, new Kerf(KerfType.Right));
            p.Variables["value"] = new VariableDefinition("value", "2+3", 5, inline: true, global: true);
            foreach (var motion in p.Codes.OfType<Motion>())
            {
                motion.UseExactStop = true;
                motion.Feedrate = 123;
                motion.VariableRefs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X"] = "value" };
            }
        }
    }

    private static void AssertGraph(Program source, Program copy)
    {
        var mapped = new Dictionary<Program, Program>(ReferenceEqualityComparer.Instance);
        var reverse = new HashSet<Program>(ReferenceEqualityComparer.Instance);
        Visit(source, copy);
        void Visit(Program original, Program owned)
        {
            Assert.NotSame(original, owned);
            if (mapped.TryGetValue(original, out var previous))
            {
                Assert.Same(previous, owned);
                return;
            }
            Assert.True(reverse.Add(owned));
            mapped.Add(original, owned);
            Assert.Equal(original.Mode, owned.Mode);
            Bits(original.Rotation, owned.Rotation);
            Assert.NotSame(original.Codes, owned.Codes);
            Assert.NotSame(original.Variables, owned.Variables);
            Assert.NotSame(original.SubPrograms, owned.SubPrograms);
            Assert.Equal(original.Variables.Keys, owned.Variables.Keys);
            foreach (var (key, value) in original.Variables)
            {
                var actual = owned.Variables[key];
                Assert.Equal(value.Name, actual.Name); Assert.Equal(value.Expression, actual.Expression);
                Bits(value.Value, actual.Value); Assert.Equal(value.Inline, actual.Inline); Assert.Equal(value.Global, actual.Global);
            }
            Assert.Equal(original.Codes.Count, owned.Codes.Count);
            for (var i = 0; i < original.Codes.Count; i++)
            {
                var code = original.Codes[i]; var actual = owned.Codes[i];
                Assert.NotSame(code, actual); Assert.Equal(code.GetType(), actual.GetType());
                if (code is Motion motion)
                {
                    var m = Assert.IsAssignableFrom<Motion>(actual);
                    Point(motion.EndPoint, m.EndPoint);
                    Assert.Equal(motion.UseExactStop, m.UseExactStop); Assert.Equal(motion.Feedrate, m.Feedrate);
                    Assert.Equal(motion.Suppressed, m.Suppressed);
                    if (motion.VariableRefs == null) Assert.Null(m.VariableRefs);
                    else
                    {
                        Assert.NotSame(motion.VariableRefs, m.VariableRefs);
                        Assert.Equal(motion.VariableRefs, m.VariableRefs);
                        Assert.Equal(motion.VariableRefs.ContainsKey("x"), m.VariableRefs!.ContainsKey("x"));
                    }
                    if (motion is LinearMove line) Assert.Equal(line.Layer, ((LinearMove)m).Layer);
                    if (motion is ArcMove arc)
                    {
                        var a = Assert.IsType<ArcMove>(m);
                        Point(arc.CenterPoint, a.CenterPoint); Assert.Equal(arc.Rotation, a.Rotation); Assert.Equal(arc.Layer, a.Layer);
                    }
                }
                if (code is Comment comment) Assert.Equal(comment.Value, Assert.IsType<Comment>(actual).Value);
                if (code is Feedrate feed)
                {
                    var f = Assert.IsType<Feedrate>(actual); Bits(feed.Value, f.Value); Assert.Equal(feed.VariableRef, f.VariableRef);
                }
                if (code is Kerf kerf) Assert.Equal(kerf.Value, Assert.IsType<Kerf>(actual).Value);
                if (code is SubProgramCall call)
                {
                    var c = Assert.IsType<SubProgramCall>(actual);
                    Assert.Equal(call.Id, c.Id); Point(call.Offset, c.Offset); Bits(call.Rotation, c.Rotation);
                    Visit(call.Program, c.Program);
                }
            }
            Assert.Equal(original.SubPrograms.Keys, owned.SubPrograms.Keys);
            foreach (var (key, child) in original.SubPrograms) Visit(child, owned.SubPrograms[key]);
        }
    }

    private static void Point(Vector expected, Vector actual) { Bits(expected.X, actual.X); Bits(expected.Y, actual.Y); }
    private static void Bits(double expected, double actual) => Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
}
