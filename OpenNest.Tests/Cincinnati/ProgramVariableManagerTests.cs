using OpenNest.CNC;

namespace OpenNest.Tests.Cincinnati;

public class ProgramVariableManagerTests
{
    [Fact]
    public void GetOrCreate_ReturnsNewVariable()
    {
        var mgr = new ProgramVariableManager();
        var v = mgr.GetOrCreate("LeadInFeedrate", 126);
        Assert.Equal(126, v.Number);
        Assert.Equal("LeadInFeedrate", v.Name);
    }

    [Fact]
    public void GetOrCreate_ReturnsSameVariable_WhenCalledTwice()
    {
        var mgr = new ProgramVariableManager();
        var v1 = mgr.GetOrCreate("LeadInFeedrate", 126);
        var v2 = mgr.GetOrCreate("LeadInFeedrate", 126);
        Assert.Same(v1, v2);
    }

    [Fact]
    public void GetOrCreate_WithExpression_SetsExpression()
    {
        var mgr = new ProgramVariableManager();
        var v = mgr.GetOrCreate("LeadInFeedrate", 126, "[#148*0.5]");
        Assert.Equal("[#148*0.5]", v.Expression);
    }

    [Fact]
    public void GetOrCreate_WithLiteral_SetsExpression()
    {
        var mgr = new ProgramVariableManager();
        var v = mgr.GetOrCreate("CircleFeedrate", 128, ".8");
        Assert.Equal(".8", v.Expression);
    }

    [Fact]
    public void Reference_ReturnsHashNumber()
    {
        var v = new ProgramVariable(126, "LeadInFeedrate");
        Assert.Equal("#126", v.Reference);
    }

    [Fact]
    public void EmitDeclarations_ProducesCorrectLines()
    {
        var mgr = new ProgramVariableManager();
        mgr.GetOrCreate("LeadInFeedrate", 126, "[#148*0.5]");
        mgr.GetOrCreate("CircleFeedrate", 128, ".8");

        var lines = mgr.EmitDeclarations();

        Assert.Contains("#126=[#148*0.5] (LEAD IN FEEDRATE)", lines);
        Assert.Contains("#128=.8 (CIRCLE FEEDRATE)", lines);
    }

    [Fact]
    public void EmitDeclarations_SkipsVariablesWithNoExpression()
    {
        var mgr = new ProgramVariableManager();
        mgr.GetOrCreate("ProcessFeedrate", 148);

        var lines = mgr.EmitDeclarations();

        Assert.Empty(lines);
    }
}
