using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Desktop.Rendering;

/// <summary>
/// What the plate view draws for one part, in its program frame before placement, chosen as the
/// WinForms <c>LayoutPart</c> chooses it: cut, etch and display-only strokes, lead strokes only for
/// manually placed lead-ins, and the material fill. Immutable once built; rebuild it when
/// <see cref="IsCurrentFor"/> says the part's program, rotation or lead-ins changed.
/// </summary>
public sealed class PartDisplayGeometry
{
    private PartDisplayGeometry(Program program, double rotation, bool manualLeadIns)
    {
        Program = program;
        Rotation = rotation;
        ManualLeadIns = manualLeadIns;
    }

    public Program Program { get; }

    public double Rotation { get; }

    public bool ManualLeadIns { get; }

    public IReadOnlyList<PathFigure> Cut { get; private init; }

    /// <summary>Lead strokes; empty unless the part has manually placed lead-ins.</summary>
    public IReadOnlyList<PathFigure> Lead { get; private init; }

    public IReadOnlyList<PathFigure> Etch { get; private init; }

    /// <summary>The filled material region: the cut outline unless break cuts, tabs or common lines make it differ.</summary>
    public IReadOnlyList<PathFigure> Material { get; private init; }

    /// <summary>The drawing's display-only geometry, rotated as placed. Never material or cut.</summary>
    public IReadOnlyList<PathFigure> Display { get; private init; }

    public bool HasOpenCuts { get; private init; }

    /// <summary>
    /// What picks the part: its material, or for an etch-only part its closed marks, so it stays
    /// selectable without treating scribe geometry as a cut or fill.
    /// </summary>
    public IReadOnlyList<PathFigure> Selection => Cut.Count > 0 ? Material : Etch;

    public bool IsCurrentFor(Part part) =>
        ReferenceEquals(Program, part.Program) && Rotation == part.Rotation
        && ManualLeadIns == part.HasManualLeadIns;

    public static PartDisplayGeometry Build(Part part)
    {
        var program = part.Program;
        var layers = ProgramGeometry.Layers(program);
        IReadOnlyList<PathFigure> material;
        // A common-line member cut once leaves its shared spans to the shared cut: its material is
        // the drawing's, rotated as placed (docs/common-line-cutting.md).
        if (CommonLineCuts.IsSinglePass(program))
        {
            var clean = (Program)part.BaseDrawing.Program.Clone();
            clean.Rotate(part.Rotation - part.BaseDrawing.Program.Rotation);
            material = ProgramGeometry.Fill(clean);
        }
        else if (layers.HasOpenCuts || ProgramGeometry.HasBreakCuts(program))
            material = ProgramGeometry.Fill(program);
        else
            material = layers.Cut;

        return new PartDisplayGeometry(program, part.Rotation, part.HasManualLeadIns)
        {
            Cut = layers.Cut,
            Lead = part.HasManualLeadIns ? layers.Lead : new List<PathFigure>(),
            Etch = layers.Etch,
            Material = material,
            Display = DisplayFigures(part),
            HasOpenCuts = layers.HasOpenCuts,
        };
    }

    // Display-only entities are kept in the drawing's program frame before rotation, so they
    // follow the part by its cumulative rotation about the program origin, then its location.
    private static List<PathFigure> DisplayFigures(Part part)
    {
        var entities = EntityRoles.DisplayGeometry(part.BaseDrawing);
        foreach (var entity in entities)
            entity.Rotate(part.Rotation);
        return ProgramGeometry.EntityFigures(entities);
    }

    /// <summary>The bounds of every drawn stroke (cut, etch, leads and display), or null when none.</summary>
    public Box StrokeBounds() => ProgramGeometry.Bounds(Cut.Concat(Etch).Concat(Lead).Concat(Display));
}
