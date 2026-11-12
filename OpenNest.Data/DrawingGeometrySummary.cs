using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Data;

/// <summary>
/// The size and hole count of a library drawing's package, measured once per package by the
/// server worker (the server never parses packages) and used to filter the library. Lengths
/// are inches whatever units the drawing was drawn in. A package that could not be measured
/// keeps a summary with <see cref="Error"/> set so it is not measured again.
/// </summary>
public sealed record DrawingGeometrySummary
{
    /// <summary>Raised when the measurement changes, so the worker measures every package again.</summary>
    public const int CurrentVersion = 1;

    public const int MaxErrorLength = 200;

    /// <summary>The <see cref="CurrentVersion"/> that measured this package.</summary>
    public int Version { get; init; }

    /// <summary>The longer side of the material's bounding box, in inches.</summary>
    public double Length { get; init; }

    /// <summary>The shorter side of the material's bounding box, in inches.</summary>
    public double Width { get; init; }

    /// <summary>Net material area (outline less holes), in square inches.</summary>
    public double Area { get; init; }

    /// <summary>Closed contours inside the outline: holes and other cutouts.</summary>
    public int Holes { get; init; }

    /// <summary>Why the package could not be measured; blank for a measured package.</summary>
    public string Error { get; init; } = "";

    public bool IsMeasured => Error.Length == 0;

    /// <summary>The first invalid field, or null when the summary may be stored.</summary>
    public string? GetValidationError()
    {
        if (Version < 1)
            return "version must be 1 or greater.";
        if (Error is null)
            return "error must be a string.";
        if (Error.Length > MaxErrorLength)
            return $"error must be at most {MaxErrorLength} characters.";
        if (!double.IsFinite(Length) || !double.IsFinite(Width) || !double.IsFinite(Area))
            return "length, width and area must be finite.";
        if (Length < 0 || Width < 0 || Area < 0 || Holes < 0)
            return "length, width, area and holes must be zero or greater.";
        if (Width > Length)
            return "width must not exceed length.";
        return null;
    }

    /// <summary>Records why a package could not be measured.</summary>
    public static DrawingGeometrySummary Failed(string error)
    {
        var text = string.IsNullOrWhiteSpace(error) ? "Could not be measured." : error.Trim();
        return new DrawingGeometrySummary
        {
            Version = CurrentVersion,
            Error = text.Length > MaxErrorLength ? text[..MaxErrorLength] : text,
        };
    }

    /// <summary>
    /// Measures a drawing's material (marks such as etch and rapids excluded): the bounding box
    /// of every closed contour, and the closed contours inside the largest one as holes.
    /// </summary>
    public static DrawingGeometrySummary Measure(Drawing drawing, Units units)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        if (drawing.Program is null)
            return Failed("No program.");

        var material = ConvertProgram.ToGeometry(drawing.Program)
            .Where(entity => SpecialLayers.IsMaterial(entity.Layer));
        var shapes = ShapeBuilder.GetShapes(material).Where(shape => shape.IsClosed()).ToList();
        if (shapes.Count == 0)
            return Failed("No closed cut geometry.");

        var outline = shapes[0];
        foreach (var shape in shapes)
        {
            if (shape.BoundingBox.Area() > outline.BoundingBox.Area())
                outline = shape;
        }

        var outer = outline.BoundingBox;
        var holes = shapes.Where(shape => shape != outline && outer.Contains(shape.BoundingBox)).ToList();
        var bounds = shapes.Select(shape => shape.BoundingBox).ToList();
        var width = bounds.Max(b => b.Right) - bounds.Min(b => b.Left);
        var height = bounds.Max(b => b.Top) - bounds.Min(b => b.Bottom);
        var area = System.Math.Abs(outline.Area()) - holes.Sum(hole => System.Math.Abs(hole.Area()));

        var scale = units == Units.Millimeters ? 1 / 25.4 : 1.0;
        return new DrawingGeometrySummary
        {
            Version = CurrentVersion,
            Length = System.Math.Round(System.Math.Max(width, height) * scale, 4),
            Width = System.Math.Round(System.Math.Min(width, height) * scale, 4),
            Area = System.Math.Round(System.Math.Max(0, area) * scale * scale, 4),
            Holes = holes.Count,
        };
    }
}
