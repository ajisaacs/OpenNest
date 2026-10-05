using System.Reflection;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class MaterialRuntimeTypeTests
{
    public static IEnumerable<object[]> UnsupportedTypes()
    {
        foreach (var kind in new[] { "program", "rapid", "linear", "arc", "call", "comment", "feedrate", "kerf", "mode" })
            foreach (var nested in new[] { false, true })
                yield return new object[] { kind, nested };
    }

    [Theory]
    [MemberData(nameof(UnsupportedTypes))]
    public void Capture_RefusesUnknownRuntimeSemanticsThroughoutExecutedGraph(string kind, bool nested)
    {
        var source = Unsupported(kind);
        if (nested)
        {
            var root = new Program();
            root.Codes.Add(new SubProgramCall(source, 0));
            source = root;
        }
        var snapshot = LeadMaterialSnapshot.Capture(source, Vector.Zero);
        Assert.False(snapshot.IsComplete);
        Assert.NotNull(snapshot.Reason);
        Assert.Throws<NotSupportedException>(() => PreparedContours.Capture(source, new CuttingParameters()));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("codes")]
    [InlineData("instruction")]
    [InlineData("missing-call")]
    [InlineData("recursive")]
    [InlineData("depth")]
    [InlineData("budget")]
    public void Capture_MalformedGraphReturnsIncompleteWithoutCrashing(string kind)
    {
        var source = LeadPathValidationTests.Rectangle(0, 0, 10, 10);
        switch (kind)
        {
            case "null": source = null!; break;
            case "codes": source.Codes = null!; break;
            case "instruction": source.Codes.Insert(0, null!); break;
            case "missing-call": source.Codes.Insert(0, new SubProgramCall()); break;
            case "recursive": source.Codes.Add(new SubProgramCall(source, 0)); break;
            case "depth":
                for (var i = 0; i < 65; i++)
                {
                    var parent = new Program();
                    parent.Codes.Add(new SubProgramCall(source, 0));
                    source = parent;
                }
                break;
            case "budget": source.Codes.InsertRange(0, Enumerable.Repeat<ICode>(new Comment(), 1000001)); break;
        }
        Assert.False(LeadMaterialSnapshot.Capture(source, Vector.Zero).IsComplete);
    }

    [Fact]
    public void Capture_CancellationStillThrows()
    {
        Assert.Throws<OperationCanceledException>(() => LeadMaterialSnapshot.Capture(
            Unsupported("linear"), Vector.Zero, new CancellationToken(true)));
    }

    [Fact]
    public void SupportedRead_MustPrecedeClone_WithoutChangingLegacyReader()
    {
        var source = Unsupported("linear");
        Assert.NotEmpty(ExecutionMotionReader.Read(source, Vector.Zero, null, default).Motions);
        Assert.Throws<NotSupportedException>(() => ExecutionMotionReader.ReadSupported(source, Vector.Zero, null));
        // Built-in Clone erases the subclass; consumers must guard the original graph.
        Assert.True(LeadMaterialSnapshot.Capture((Program)source.Clone(), Vector.Zero).IsComplete);
        Assert.False(LeadMaterialSnapshot.Capture(source, Vector.Zero).IsComplete);
    }

    private static Program Unsupported(string kind)
    {
        var source = LeadPathValidationTests.Rectangle(0, 0, 10, 10);
        switch (kind)
        {
            case "program":
                var custom = new CustomProgram();
                custom.Codes.AddRange(source.Codes);
                return custom;
            case "rapid": source.Codes[0] = new CustomRapid { EndPoint = ((Motion)source.Codes[0]).EndPoint }; break;
            case "linear": source.Codes[1] = new CustomLinear { EndPoint = ((Motion)source.Codes[1]).EndPoint }; break;
            case "arc":
                source = new Program();
                source.MoveTo(1, 0);
                source.Codes.Add(new CustomArc { EndPoint = new Vector(1, 0), CenterPoint = Vector.Zero });
                break;
            case "call":
                var caller = new Program();
                caller.Codes.Add(new CustomCall { Program = source });
                return caller;
            case "comment": source.Codes.Insert(0, new CustomComment()); break;
            case "feedrate": source.Codes.Insert(0, new CustomFeedrate()); break;
            case "kerf": source.Codes.Insert(0, new CustomKerf()); break;
            case "mode": typeof(Program).GetField("mode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(source, (Mode)100); break;
        }
        return source;
    }

    private sealed class CustomProgram : Program { }
    private sealed class CustomRapid : RapidMove { }
    private sealed class CustomLinear : LinearMove { }
    private sealed class CustomArc : ArcMove { }
    private sealed class CustomCall : SubProgramCall { }
    private sealed class CustomComment : Comment { }
    private sealed class CustomFeedrate : Feedrate { }
    private sealed class CustomKerf : Kerf { }
}
