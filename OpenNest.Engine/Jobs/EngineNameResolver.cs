#nullable enable
using System;
using OpenNest.Engine.Jobs.Placement;

namespace OpenNest.Engine.Jobs;

public enum EngineNameKind
{
    FillStrategy,
    JobEngine,
    Unknown,
}

/// <param name="Name">The trimmed caller name.</param>
/// <param name="FillStrategy">Canonical strategy name when <see cref="Kind"/> is FillStrategy.</param>
public readonly record struct EngineNameClassification(EngineNameKind Kind, string Name, string? FillStrategy)
{
    public bool IsFillStrategy => Kind == EngineNameKind.FillStrategy;
    public bool IsJobEngine => Kind == EngineNameKind.JobEngine;
}

/// <summary>
/// Classifies a name from a front end selector that accepts both single-plate fill strategies
/// (<see cref="PlateFillService"/>) and whole-job engines (<see cref="NestingEngineRegistry"/>).
/// Fill strategies win, so "Fill" and "Strip" resolve as strategies; an omitted name means
/// <see cref="PlateFillService.DefaultStrategy"/>. Front ends keep their own message wording.
/// </summary>
public static class EngineNameResolver
{
    public static EngineNameClassification Classify(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        try
        {
            return new(EngineNameKind.FillStrategy, trimmed, PlateFillService.ResolveStrategy(trimmed));
        }
        catch (NotSupportedException)
        {
            var kind = NestingEngineRegistry.ResolveName(trimmed) != null
                ? EngineNameKind.JobEngine
                : EngineNameKind.Unknown;
            return new(kind, trimmed, null);
        }
    }
}
