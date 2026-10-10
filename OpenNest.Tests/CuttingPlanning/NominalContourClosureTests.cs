using OpenNest.CNC.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class NominalContourClosureTests
{
    [Theory]
    [InlineData(0.0000009, true)]
    [InlineData(0.000001, true)]
    [InlineData(0.00000101, false)]
    [InlineData(0.000002, false)]
    public void CaptureAndPreparation_AgreeAtClosureBoundary(double gap, bool accepted)
    {
        // Nonzero coordinates exercise roundoff at the inclusive boundary.
        var program = new OpenNest.CNC.Program();
        program.MoveTo(14, 3);
        program.LineTo(24, 3);
        program.LineTo(24, 13);
        program.LineTo(14, 13);
        program.LineTo(14, 3 + gap);
        var original = OwnedProgramCopy.Copy(program);

        var material = LeadMaterialSnapshot.Capture(program, new Vector(30, 20));

        Assert.Equal(accepted, material.IsComplete);
        if (accepted)
            Assert.Equal(1, PreparedContours.Capture(program, ExplicitContourTests.Parameters()).Count);
        else
        {
            Assert.Contains("closure tolerance 1E-06", material.Reason);
            Assert.Throws<ArgumentException>(() => PreparedContours.Capture(program, ExplicitContourTests.Parameters()));
        }
        Assert.True(ProgramContent.Equal(original, program));
    }
}
