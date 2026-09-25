using System.Collections.Generic;
using OpenNest.Engine.Jobs;

namespace OpenNest.Benchmark;

/// <summary>Benchmark validation outcome.</summary>
public class ValidationResult
{
    public bool Valid => Violations.Count == 0;
    public List<string> Violations { get; } = new();
}

/// <summary>Compatibility wrapper over the shared layout validation contract.</summary>
public static class NestValidator
{
    /// <summary>Checks materialized plates using drawing-reference requirement identity.</summary>
    public static ValidationResult Validate(
        List<(Plate Plate, List<Part> Parts)> plateRuns,
        IReadOnlyDictionary<Drawing, (string Name, int Quantity)> requirements)
    {
        var result = new ValidationResult();
        result.Violations.AddRange(NestLayoutCheck.Validate(plateRuns, requirements));
        return result;
    }

    /// <summary>Appends offered-stock, finite-stock and rotation-policy violations.</summary>
    public static void ValidateAgainstJob(NestJob job, NestJobResult jobResult,
        IReadOnlyDictionary<string, string> displayNames, ValidationResult result) =>
        NestLayoutCheck.ValidateAgainstJob(job, jobResult, displayNames, result.Violations);
}
