using System;

namespace OpenNest.Desktop.Interaction;

/// <summary>
/// Keeps one plate tool active, as the WinForms view's action manager does: Select by default;
/// Escape first cancels what the active tool is doing, then ends the tool and returns to Select,
/// and an Escape from an idle Select brings back the tool Escape ended (when that tool allows it).
/// <see cref="Status"/> is the active tool's name and hint for the status bar.
/// </summary>
public sealed class PlateToolManager
{
    private readonly IPlateToolHost host;
    private PlateTool previous;

    public PlateToolManager(IPlateToolHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        Current = new SelectTool(host);
        Current.HintChanged += OnHintChanged;
        Current.Activate();
        Status = StatusOf(Current);
    }

    /// <summary>The active tool.</summary>
    public PlateTool Current { get; private set; }

    /// <summary>The active tool's name, and its hint when it has one.</summary>
    public string Status { get; private set; }

    /// <summary>Raised when the active tool or its status changes.</summary>
    public event EventHandler StatusChanged;

    /// <summary>
    /// Makes <paramref name="tool"/> active, ending the current one. Ending another tool for
    /// Select keeps it for Escape to resume; any other switch forgets the resumable tool.
    /// </summary>
    public void SetTool(PlateTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        previous = tool is SelectTool && Current is not SelectTool && Current.ResumeOnEscape ? Current : null;
        Switch(tool);
    }

    /// <summary>Returns to Select (as a fresh tool, so nothing selected is kept).</summary>
    public void Select() => SetTool(new SelectTool(host));

    /// <summary>
    /// Ends the active tool at the user's explicit request (a panel's Close button, say). Unlike
    /// Escape, the ended tool is not kept for Escape to resume.
    /// </summary>
    public void EndTool()
    {
        Select();
        previous = null;
    }

    /// <summary>Handles Escape; see the class summary.</summary>
    public void Escape()
    {
        if (Current.IsBusy)
            Current.Cancel();
        else if (Current is SelectTool && previous != null)
        {
            var resumed = previous;
            previous = null;
            Switch(resumed);
        }
        else
            Select();
        host.Invalidate();
    }

    /// <summary>Called when the host shows another plate: tools that do not survive it end.</summary>
    public void OnPlateChanged()
    {
        if (!Current.SurvivesPlateChange)
            Select();
        else
            Current.OnPlateChanged();
    }

    private void Switch(PlateTool tool)
    {
        var old = Current;
        old.Cancel();
        old.Deactivate();
        old.HintChanged -= OnHintChanged;
        Current = tool;
        tool.HintChanged += OnHintChanged;
        tool.Activate();
        RefreshStatus();
        host.Invalidate();
    }

    private void OnHintChanged(object sender, EventArgs e)
    {
        if (ReferenceEquals(sender, Current))
            RefreshStatus();
    }

    private void RefreshStatus()
    {
        Status = StatusOf(Current);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string StatusOf(PlateTool tool) =>
        string.IsNullOrEmpty(tool.Hint) ? tool.Name : $"{tool.Name}: {tool.Hint}";
}
