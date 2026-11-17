using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Engine.Fill;

namespace OpenNest.Desktop.Interaction;

/// <summary>
/// Edit &gt; Cut and Copy, toolkit-neutral: both start a <see cref="PlaceTool"/> with the parts
/// selected on the plate, in selection order. Neither does anything without a plate or selected
/// parts, or while the host's edits are locked.
/// </summary>
public static class PlateClipboard
{
    /// <summary>
    /// Starts placing copies of the selected parts; null when nothing was started.
    /// </summary>
    public static PlaceTool Copy(
        IPlateToolHost host, PlateToolManager tools, Func<SettleOrder> order)
    {
        var sources = Sources(host, tools);
        if (sources == null)
            return null;
        var tool = new PlaceTool(host, sources, order);
        tools.SetTool(tool);
        return tool;
    }

    /// <summary>
    /// Starts placing copies of the selected parts, then removes them from the plate as one
    /// "Cut" undo step. As in WinForms, the plate's cut-offs are not rebuilt here.
    /// </summary>
    public static PlaceTool Cut(
        IPlateToolHost host, PlateToolManager tools, Func<SettleOrder> order)
    {
        var sources = Sources(host, tools);
        if (sources == null)
            return null;
        // The tool reads the sources' common-line groups, which their removal ends.
        var tool = new PlaceTool(host, sources, order);
        tools.SetTool(tool);

        var plate = host.Plate;
        using var change = host.BeginChange("Cut");
        change.MarkChanged();
        foreach (var part in sources)
            plate.Parts.Remove(part);
        host.Selection.Clear();
        host.Invalidate();
        change.Complete();
        return tool;
    }

    private static List<Part> Sources(IPlateToolHost host, PlateToolManager tools)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(tools);
        if (host.Plate == null || host.EditsLocked)
            return null;
        host.Selection.Prune();
        var sources = host.Selection.Parts.ToList();
        return sources.Count == 0 ? null : sources;
    }
}
