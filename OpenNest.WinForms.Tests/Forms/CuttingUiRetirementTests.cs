using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

public class CuttingUiRetirementTests
{
    [Fact]
    public void EditorAssemblyHasOnlyTheDockedInspectionEntryPoints()
    {
        var assembly = typeof(EditNestForm).Assembly;
        Assert.Null(assembly.GetType("OpenNest.Forms.CuttingPlanForm"));
        Assert.Null(assembly.GetType("OpenNest.Forms.ValidateNestForm"));
        Assert.Null(typeof(EditNestForm).GetMethod("PlanCutting"));
        // Non-editor consumers retain the shared analyzer, not an editor-dependent inspector.
        Assert.Contains(typeof(OpenNest.Diagnostics.PostVerificationAnalyzer).GetMethods(),
            method => method.Name == "Analyze" && method.GetParameters().Length > 0
                && method.GetParameters()[0].ParameterType == typeof(Nest));
    }
}
