using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Diagnostics;

/// <summary>
/// Reuses each clean drawing program's converted entities and prepared (validated, chorded,
/// triangulation-checked) material across overlap requests. Preparation dominates the cost of
/// drawings with many holes, and it depends only on the drawing, not on where parts sit, so a
/// recheck after moving parts only repeats the cheap pose transforms and pair clipping.
/// Entries are keyed by <see cref="Program"/> reference and released with it. A changed code
/// count or program rotation is detected, but that is not a geometry hash: call
/// <see cref="Clear"/> before any in-place edit of a clean program or its hole subprograms.
/// Capture on one thread at a time; prepared entries may be shared by concurrent analyses.
/// </summary>
public sealed class OverlapMaterialCache
{
    private readonly ConditionalWeakTable<Program, OverlapSource> sources = new();

    public void Clear() => sources.Clear();

    internal OverlapSource Get(Program program, Func<Program, OverlapSource> create)
    {
        if (sources.TryGetValue(program, out var source) && source.Matches(program))
            return source;
        source = create(program);
        sources.AddOrUpdate(program, source);
        return source;
    }
}

/// <summary>Owned converted entities for one clean program, plus its lazily prepared material.</summary>
internal sealed class OverlapSource
{
    private readonly object gate = new();
    private readonly int codeCount;
    private readonly long rotation;
    private PreparedMaterial prepared;

    internal OverlapSource(Program program, List<Entity> entities, string error)
    {
        codeCount = program.Codes.Count;
        rotation = BitConverter.DoubleToInt64Bits(program.Rotation);
        Entities = entities;
        Error = error;
    }

    /// <summary>Never mutated; preparation clones before chaining.</summary>
    internal List<Entity> Entities { get; }
    internal string Error { get; }

    internal bool Matches(Program program) =>
        program.Codes.Count == codeCount && BitConverter.DoubleToInt64Bits(program.Rotation) == rotation;

    /// <summary>
    /// Prepares once and shares the result. A geometry failure is cached like a success;
    /// cancellation is not, so a superseded request cannot poison the next one.
    /// </summary>
    internal PreparedMaterial Prepare(CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref prepared);
        if (current != null)
            return current;
        lock (gate)
        {
            if (prepared != null)
                return prepared;
            PreparedMaterial result;
            try
            {
                result = new PreparedMaterial(OverlapMaterial.Read(Entities, cancellationToken), null);
            }
            catch (Exception exception) when (PlateOverlapAnalyzer.IsGeometryFailure(exception))
            {
                result = new PreparedMaterial(null, exception.Message);
            }
            Volatile.Write(ref prepared, result);
            return result;
        }
    }
}

/// <summary>Validated local-frame material, only ever read (transformed into new polygons).</summary>
internal sealed record PreparedMaterial(OverlapMaterial Material, string Error);
