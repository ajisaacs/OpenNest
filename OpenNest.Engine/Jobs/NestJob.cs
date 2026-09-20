using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest;

/// <summary>One material/unit system's requirements. Collections are copied; all nested values are immutable.</summary>
public sealed class NestJob
{
    public NestJob(
        IEnumerable<NestJobPart> parts,
        IEnumerable<NestPlateStock> plates,
        NestJobOptions options = null
    )
    {
        Parts = Own(parts);
        Plates = Own(plates);
        Options = options ?? new NestJobOptions();
        if (
            Parts.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != Parts.Count
            || Plates.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != Plates.Count
        )
            throw new ArgumentException("Part and stock IDs must each be unique.");
    }

    public IReadOnlyList<NestJobPart> Parts { get; }
    public IReadOnlyList<NestPlateStock> Plates { get; }
    public NestJobOptions Options { get; }

    internal static IReadOnlyList<T> Own<T>(IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var values = source.ToArray();
        if (values.Any(value => value is null))
            throw new ArgumentException("Null entries are not allowed.", nameof(source));
        return Array.AsReadOnly(values);
    }
}
