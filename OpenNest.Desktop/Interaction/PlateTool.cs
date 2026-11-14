using System;
using OpenNest.Desktop.Rendering;
using OpenNest.Geometry;

namespace OpenNest.Desktop.Interaction;

/// <summary>The mouse button behind a pointer event.</summary>
public enum ToolButton
{
    None,
    Left,
    Middle,
    Right,
}

/// <summary>Modifier keys held during a pointer event.</summary>
[Flags]
public enum ToolModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
}

/// <summary>
/// A pointer event as a plate tool sees it: where it is in plate coordinates and on screen, the
/// button pressed or released (for a move, the button held), and the modifier keys.
/// </summary>
public readonly record struct ToolPointer(Vector World, ScreenPoint Screen, ToolButton Button, ToolModifiers Modifiers);

/// <summary>What a plate tool works on: the view that hosts it, in either desktop app.</summary>
public interface IPlateToolHost
{
    /// <summary>The plate shown, or null.</summary>
    Plate Plate { get; }

    PlateSelection Selection { get; }

    PlateHitTester HitTester { get; }

    /// <summary>The view's world/screen mapping, for tolerances given in pixels.</summary>
    ViewTransform View { get; }

    /// <summary>Requests a redraw.</summary>
    void Invalidate();
}

/// <summary>
/// One interactive plate-view tool, toolkit-neutral: the view forwards pointer events to the
/// active tool and draws what it exposes. Exactly one tool is active at a time; see
/// <see cref="PlateToolManager"/>.
/// </summary>
public abstract class PlateTool
{
    protected PlateTool(IPlateToolHost host)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
    }

    protected IPlateToolHost Host { get; }

    /// <summary>The name shown in the status bar.</summary>
    public abstract string Name { get; }

    /// <summary>Status bar guidance shown after the name, or null for the name alone.</summary>
    public virtual string Hint => null;

    /// <summary>Whether the tool is mid-task, so Escape cancels that task rather than ending the tool.</summary>
    public abstract bool IsBusy { get; }

    /// <summary>Whether Escape from Select may bring this tool back after it ends.</summary>
    public virtual bool ResumeOnEscape => true;

    /// <summary>Whether the tool stays active when the view shows another plate.</summary>
    public virtual bool SurvivesPlateChange => false;

    /// <summary>Raised when <see cref="Hint"/> changes.</summary>
    public event EventHandler HintChanged;

    /// <summary>Called when the tool becomes active (again, when Escape resumes it).</summary>
    public virtual void Activate() { }

    /// <summary>Called when the tool stops being active, after <see cref="Cancel"/>.</summary>
    public virtual void Deactivate() { }

    /// <summary>Abandons the task in progress.</summary>
    public abstract void Cancel();

    /// <summary>Called when a tool that survives plate changes sees another plate.</summary>
    public virtual void OnPlateChanged() { }

    /// <returns>Whether the tool used the event.</returns>
    public virtual bool PointerPressed(ToolPointer pointer) => false;

    /// <returns>Whether the tool used the event.</returns>
    public virtual bool PointerMoved(ToolPointer pointer) => false;

    /// <returns>Whether the tool used the event.</returns>
    public virtual bool PointerReleased(ToolPointer pointer) => false;

    protected void RaiseHintChanged() => HintChanged?.Invoke(this, EventArgs.Empty);
}
