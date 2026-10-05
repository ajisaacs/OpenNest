using System;
using System.Collections.Generic;
using System.Threading;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>
/// Exact structural equality of two program graphs: instruction runtime types, scalar bits,
/// motion metadata, variables and the same instruction/sub-program sharing shape. This is
/// a freshness check, not geometric equivalence; unsupported instruction types never match.
/// </summary>
internal static class ProgramContent
{
    internal static bool Equal(Program left, Program right, CancellationToken token = default)
    {
        var programs = new Dictionary<Program, Program>(ReferenceEqualityComparer.Instance);
        var programOwners = new HashSet<Program>(ReferenceEqualityComparer.Instance);
        var codes = new Dictionary<ICode, ICode>(ReferenceEqualityComparer.Instance);
        var codeOwners = new HashSet<ICode>(ReferenceEqualityComparer.Instance);
        return SameProgram(left, right);

        bool SameProgram(Program a, Program b)
        {
            token.ThrowIfCancellationRequested();
            if (a == null || b == null)
                return a == null && b == null;
            // Pair graphs one-to-one so shared and distinct sub-programs cannot be confused.
            if (programs.TryGetValue(a, out var paired))
                return ReferenceEquals(paired, b);
            if (!programOwners.Add(b))
                return false;
            programs.Add(a, b);
            if (a.GetType() != b.GetType() || a.Mode != b.Mode || !Bits(a.Rotation, b.Rotation)
                || a.Codes.Count != b.Codes.Count || a.SubPrograms.Count != b.SubPrograms.Count
                || !SameVariables(a.Variables, b.Variables))
                return false;
            for (var i = 0; i < a.Codes.Count; i++)
                if (!SameCode(a.Codes[i], b.Codes[i]))
                    return false;
            foreach (var (id, child) in a.SubPrograms)
                if (!b.SubPrograms.TryGetValue(id, out var other) || !SameProgram(child, other))
                    return false;
            return true;
        }

        bool SameCode(ICode a, ICode b)
        {
            token.ThrowIfCancellationRequested();
            if (a == null || b == null)
                return a == null && b == null;
            if (codes.TryGetValue(a, out var paired))
                return ReferenceEquals(paired, b);
            if (!codeOwners.Add(b))
                return false;
            codes.Add(a, b);
            if (a.GetType() != b.GetType())
                return false;
            return (a, b) switch
            {
                (RapidMove x, RapidMove y) => SameMotion(x, y),
                (LinearMove x, LinearMove y) => SameMotion(x, y) && x.Layer == y.Layer,
                (ArcMove x, ArcMove y) => SameMotion(x, y) && x.Layer == y.Layer
                    && x.Rotation == y.Rotation && Bits(x.CenterPoint, y.CenterPoint),
                (SubProgramCall x, SubProgramCall y) => x.Id == y.Id && Bits(x.Offset, y.Offset)
                    && Bits(x.Rotation, y.Rotation) && SameProgram(x.Program, y.Program),
                (Comment x, Comment y) => string.Equals(x.Value, y.Value, StringComparison.Ordinal),
                (Feedrate x, Feedrate y) => Bits(x.Value, y.Value)
                    && string.Equals(x.VariableRef, y.VariableRef, StringComparison.Ordinal),
                (Kerf x, Kerf y) => x.Value == y.Value,
                _ => false
            };
        }
    }

    private static bool SameMotion(Motion a, Motion b) => Bits(a.EndPoint, b.EndPoint)
        && a.UseExactStop == b.UseExactStop && a.Feedrate == b.Feedrate && a.Suppressed == b.Suppressed
        && SameRefs(a.VariableRefs, b.VariableRefs);

    private static bool SameRefs(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        if (a == null || b == null)
            return a == null && b == null;
        if (a.Count != b.Count || !a.Comparer.Equals(b.Comparer))
            return false;
        foreach (var (key, value) in a)
            if (!b.TryGetValue(key, out var other) || !string.Equals(value, other, StringComparison.Ordinal))
                return false;
        return true;
    }

    private static bool SameVariables(Dictionary<string, VariableDefinition> a, Dictionary<string, VariableDefinition> b)
    {
        if (a.Count != b.Count || !a.Comparer.Equals(b.Comparer))
            return false;
        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other))
                return false;
            if (ReferenceEquals(value, other))
                continue;
            if (value == null || other == null
                || !string.Equals(value.Name, other.Name, StringComparison.Ordinal)
                || !string.Equals(value.Expression, other.Expression, StringComparison.Ordinal)
                || !Bits(value.Value, other.Value) || value.Inline != other.Inline || value.Global != other.Global)
                return false;
        }
        return true;
    }

    private static bool Bits(Vector a, Vector b) => Bits(a.X, b.X) && Bits(a.Y, b.Y);

    private static bool Bits(double a, double b) =>
        BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
}
