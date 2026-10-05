using System.Reflection;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class CuttingCaptureFidelityTests
{
    public static IEnumerable<object[]> UnsupportedOriginals()
    {
        foreach (var kind in new[] { "linear", "program", "mode" })
            foreach (var nested in new[] { false, true })
                foreach (var target in new[] { "eligible", "locked", "ineligible", "placed", "compatibility" })
                    yield return new object[] { kind, nested, target };
    }

    [Theory]
    [MemberData(nameof(UnsupportedOriginals))]
    public void Service_RefusesOriginalUnsupportedSemanticsBeforeClone(string kind, bool nested, string target)
    {
        var original = Rectangle();
        if (kind == "linear")
            original.Codes[1] = new CustomLinear { EndPoint = ((Motion)original.Codes[1]).EndPoint };
        if (kind == "program")
        {
            var custom = new CustomProgram();
            custom.Codes.AddRange(original.Codes);
            original = custom;
        }
        var invalid = original;
        if (nested)
        {
            var root = new Program();
            root.Codes.Add(new SubProgramCall(original, 0) { Id = -3 });
            original = root;
        }
        // Part construction uses legacy Clone, so original subclass information is lost.
        var part = new Part(new Drawing("original", original));
        if (kind == "linear")
        {
            var placed = nested ? ((SubProgramCall)part.Program.Codes[0]).Program : part.Program;
            Assert.IsType<LinearMove>(placed.Codes[1]);
        }
        if (target == "placed")
        {
            part = new Part(new Drawing("supported", Rectangle()));
            Assert.True(part.RestoreLeadInProgram(original, false));
        }
        if (kind == "mode")
            typeof(Program).GetField("mode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(invalid, (Mode)100);
        part.LeadInsLocked = target == "locked";
        var clean = part.BaseDrawing.Program;
        var placedProgram = part.Program;
        var codes = original.Codes.ToArray();
        var invalidCodes = invalid.Codes.ToArray();
        var bounds = part.BoundingBox;
        var quantity = part.BaseDrawing.Quantity.Nested;
        var cloneCalls = invalid.Codes.OfType<CustomLinear>().Sum(c => c.CloneCalls);
        var result = CuttingPlanService.Plan(new CuttingPlanRequest([part],
            confirmedParameters: target == "compatibility" ? null : ExplicitContourTests.Parameters(),
            eligibleParts: target == "ineligible" ? [] : null));
        Assert.Equal(CuttingPlanStatus.UnsupportedGeometry, result.Status);
        Assert.Empty(result.ProposedOrder);
        Assert.False(result.IndependentlyReplayed);
        Assert.Contains(result.Findings, f => ReferenceEquals(f.SourcePart, part) && f.SourceOrdinal == 0);
        Assert.Same(clean, part.BaseDrawing.Program);
        Assert.Same(placedProgram, part.Program);
        Assert.Equal(codes, original.Codes);
        Assert.Equal(invalidCodes, invalid.Codes);
        Assert.Same(bounds, part.BoundingBox);
        Assert.Equal(quantity, part.BaseDrawing.Quantity.Nested);
        Assert.Equal(cloneCalls, invalid.Codes.OfType<CustomLinear>().Sum(c => c.CloneCalls));
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("program")]
    [InlineData("mode")]
    public void Service_RefusesUnsupportedInactiveRegisteredGraphBeforeClone(string kind)
    {
        var part = new Part(new Drawing("registered", Rectangle()));
        Program inactive = kind == "program" ? new CustomProgram() : new Program();
        if (kind == "linear") inactive.Codes.Add(new CustomLinear());
        if (kind == "mode")
            typeof(Program).GetField("mode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(inactive, (Mode)100);
        part.Program.SubPrograms[-27] = inactive;
        var result = CuttingPlanService.Plan(new CuttingPlanRequest([part]));
        Assert.Equal(CuttingPlanStatus.UnsupportedGeometry, result.Status);
        Assert.Empty(result.ProposedOrder);
        Assert.Same(inactive, part.Program.SubPrograms[-27]);
        Assert.All(inactive.Codes.OfType<CustomLinear>(), c => Assert.Equal(0, c.CloneCalls));
    }

    private static Program Rectangle() => LeadPathValidationTests.Rectangle(0, 0, 10, 10);
    private sealed class CustomProgram : Program { }
    private sealed class CustomLinear : LinearMove
    {
        public int CloneCalls { get; private set; }
        public override ICode Clone()
        {
            CloneCalls++;
            return base.Clone();
        }
    }
}
