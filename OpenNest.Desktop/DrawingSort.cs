using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Desktop;

/// <summary>How the Drawings tab orders its list.</summary>
public enum DrawingSortOrder
{
    Name,
    Area,
    BoundingBox,
    Remaining,
}

/// <summary>
/// The Drawings tab's order and its Hide Fully Nested Drawings filter. Every order but Name
/// puts the largest value first; ties fall back to the name so the list never shuffles.
/// The choice is stored by enum name in the DrawingSortOrder user setting.
/// </summary>
public static class DrawingSort
{
    public const string SettingName = "DrawingSortOrder";

    /// <summary>The sort menu's choices, in menu order.</summary>
    public static IReadOnlyList<(DrawingSortOrder Order, string Text)> Choices { get; } = new[]
    {
        (DrawingSortOrder.Name, "Name"),
        (DrawingSortOrder.Area, "Area (largest first)"),
        (DrawingSortOrder.BoundingBox, "Bounding Box Size (largest first)"),
        (DrawingSortOrder.Remaining, "Remaining Qty (most first)"),
    };

    /// <summary>The stored enum name's order; an unknown name sorts by name.</summary>
    public static DrawingSortOrder Parse(string stored) =>
        Enum.TryParse<DrawingSortOrder>(stored, out var order) && Enum.IsDefined(order)
            ? order
            : DrawingSortOrder.Name;

    /// <summary>A drawing Hide Fully Nested Drawings hides: one with a quantity, all of it nested.</summary>
    public static bool IsFullyNested(Drawing drawing) =>
        drawing.Quantity.Required > 0 && drawing.Quantity.Remaining == 0;

    /// <summary>
    /// <paramref name="drawings"/> in <paramref name="order"/>, leaving out the fully nested
    /// ones when <paramref name="hideFullyNested"/> is set.
    /// </summary>
    public static List<Drawing> Arrange(IEnumerable<Drawing> drawings, DrawingSortOrder order, bool hideFullyNested)
    {
        ArgumentNullException.ThrowIfNull(drawings);
        var shown = hideFullyNested ? drawings.Where(d => !IsFullyNested(d)) : drawings;

        var sorted = order switch
        {
            DrawingSortOrder.Area => shown.OrderByDescending(d => d.Area).ThenBy(d => d.Name),
            DrawingSortOrder.BoundingBox => shown.OrderByDescending(BoundingBoxArea).ThenBy(d => d.Name),
            DrawingSortOrder.Remaining => shown.OrderByDescending(d => d.Quantity.Remaining).ThenBy(d => d.Name),
            _ => shown.OrderBy(d => d.Name),
        };
        return sorted.ToList();
    }

    private static double BoundingBoxArea(Drawing drawing)
    {
        var size = drawing.Program.BoundingBox().Size;
        return size.Width * size.Length;
    }
}
