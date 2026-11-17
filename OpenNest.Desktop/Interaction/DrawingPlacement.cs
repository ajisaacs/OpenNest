using System;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;

namespace OpenNest.Desktop.Interaction;

/// <summary>
/// Adding a part from the nest's drawing list, as the WinForms nest window does: dropping a
/// dragged drawing on the plate adds one part centred where it was dropped, and a drawing
/// picked in the list then carried into the plate view starts the place tool with it.
/// </summary>
public static class DrawingPlacement
{
    /// <summary>
    /// A drawing dropped on the plate: one part with its bounding box centred on
    /// <paramref name="at"/>, as one "Add Part" undo step; null without a plate or while edits
    /// are locked. As in WinForms, the plate's cut-offs are not rebuilt.
    /// </summary>
    public static Part AddPart(IPlateToolHost host, Drawing drawing, Vector at)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(drawing);
        var plate = host.Plate;
        if (plate == null || host.EditsLocked)
            return null;

        var part = new Part(drawing, at);
        var center = part.BoundingBox.Center;
        part.Offset(at.X - center.X, at.Y - center.Y);

        using var change = host.BeginChange("Add Part");
        change.MarkChanged();
        plate.Parts.Add(part);
        host.Invalidate();
        change.Complete();
        return part;
    }

    /// <summary>The place tool with one new part of <paramref name="drawing"/>.</summary>
    public static PlaceTool CreatePlaceTool(
        IPlateToolHost host, Drawing drawing, Func<SettleOrder> placementOrder)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(drawing);
        return new PlaceTool(host, new[] { new Part(drawing) }, placementOrder);
    }
}
