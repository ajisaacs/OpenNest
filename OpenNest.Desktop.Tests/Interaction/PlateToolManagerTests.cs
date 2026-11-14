using OpenNest.Desktop.Interaction;

namespace OpenNest.Desktop.Tests.Interaction;

public class PlateToolManagerTests
{
    private readonly TestToolHost host = new(InteractionFixtures.Plate());

    private PlateToolManager Tools => host.Tools;

    private sealed class FakeTool : PlateTool
    {
        private string hint;

        public FakeTool(IPlateToolHost host, bool resume = true, bool survives = false)
            : base(host)
        {
            ResumeOnEscape = resume;
            SurvivesPlateChange = survives;
        }

        public override string Name => "Fake";

        public override string Hint => hint;

        public override bool ResumeOnEscape { get; }

        public override bool SurvivesPlateChange { get; }

        public bool Busy { get; set; }

        public override bool IsBusy => Busy;

        public int Activations { get; private set; }

        public int Deactivations { get; private set; }

        public int Cancels { get; private set; }

        public int PlateChanges { get; private set; }

        public override void Activate() => Activations++;

        public override void Deactivate() => Deactivations++;

        public override void Cancel()
        {
            Cancels++;
            Busy = false;
        }

        public override void OnPlateChanged() => PlateChanges++;

        public void SetHint(string value)
        {
            hint = value;
            RaiseHintChanged();
        }
    }

    [Fact]
    public void Select_is_active_from_the_start_and_shows_its_name()
    {
        Assert.IsType<SelectTool>(Tools.Current);
        Assert.Equal("Select", Tools.Status);
    }

    [Fact]
    public void One_tool_is_active_and_its_hint_follows_its_name()
    {
        var statuses = new List<string>();
        Tools.StatusChanged += (_, _) => statuses.Add(Tools.Status);
        var tool = new FakeTool(host);

        Tools.SetTool(tool);
        tool.SetHint("pick a point");

        Assert.Same(tool, Tools.Current);
        Assert.Equal(1, tool.Activations);
        Assert.Equal(new[] { "Fake", "Fake: pick a point" }, statuses);
    }

    [Fact]
    public void Switching_tools_cancels_and_deactivates_the_old_one()
    {
        var first = new FakeTool(host);
        Tools.SetTool(first);
        Tools.SetTool(new FakeTool(host));

        Assert.Equal(1, first.Cancels);
        Assert.Equal(1, first.Deactivations);

        first.SetHint("stale");
        Assert.Equal("Fake", Tools.Status);
    }

    [Fact]
    public void Escape_cancels_a_busy_tool_before_ending_it()
    {
        var tool = new FakeTool(host) { Busy = true };
        Tools.SetTool(tool);

        Tools.Escape();
        Assert.Same(tool, Tools.Current);
        Assert.Equal(1, tool.Cancels);

        Tools.Escape();
        Assert.IsType<SelectTool>(Tools.Current);
        Assert.Equal("Select", Tools.Status);
    }

    [Fact]
    public void Escape_from_an_idle_Select_resumes_the_tool_Escape_ended()
    {
        var tool = new FakeTool(host);
        Tools.SetTool(tool);
        Tools.Escape();

        Tools.Escape();

        Assert.Same(tool, Tools.Current);
        Assert.Equal(2, tool.Activations);
    }

    [Fact]
    public void Escape_from_Select_first_clears_the_selection_before_resuming()
    {
        var tool = new FakeTool(host);
        Tools.SetTool(tool);
        Tools.Escape();
        host.Click(25, 5);

        Tools.Escape();
        Assert.IsType<SelectTool>(Tools.Current);
        Assert.True(host.Selection.IsEmpty);

        Tools.Escape();
        Assert.Same(tool, Tools.Current);
    }

    [Fact]
    public void A_tool_that_declines_resuming_is_not_brought_back()
    {
        Tools.SetTool(new FakeTool(host, resume: false));
        Tools.Escape();

        Tools.Escape();

        Assert.IsType<SelectTool>(Tools.Current);
    }

    [Fact]
    public void Ending_a_tool_explicitly_forgets_it()
    {
        Tools.SetTool(new FakeTool(host));

        Tools.EndTool();
        Tools.Escape();

        Assert.IsType<SelectTool>(Tools.Current);
    }

    [Fact]
    public void Another_plate_ends_tools_that_do_not_survive_it()
    {
        var survivor = new FakeTool(host, survives: true);
        Tools.SetTool(survivor);
        Tools.OnPlateChanged();
        Assert.Same(survivor, Tools.Current);
        Assert.Equal(1, survivor.PlateChanges);

        Tools.SetTool(new FakeTool(host));
        Tools.OnPlateChanged();
        Assert.IsType<SelectTool>(Tools.Current);
    }

    [Fact]
    public void Escape_with_nothing_selected_in_Select_stays_in_Select()
    {
        var before = Tools.Current;

        Tools.Escape();

        Assert.IsType<SelectTool>(Tools.Current);
        Assert.NotSame(before, Tools.Current);
    }
}
