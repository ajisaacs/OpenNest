using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OpenNest.CNC;

public readonly record struct HighlightSpan(int Index, int Length, int RuleIndex);

/// <summary>Computes cosmetic G-code highlight spans without changing the generated text.</summary>
public static class ProgramHighlighting
{
    private static readonly Regex[] Rules =
    {
        new(@"^;.*$", RegexOptions.Multiline, TimeSpan.FromMilliseconds(100)),
        new(@"^G9[01]\b", RegexOptions.Multiline, TimeSpan.FromMilliseconds(100)),
        new(@"^G00\b", RegexOptions.Multiline, TimeSpan.FromMilliseconds(100)),
        new(@"^G01\b", RegexOptions.Multiline, TimeSpan.FromMilliseconds(100)),
        new(@"^G0[23]\b", RegexOptions.Multiline, TimeSpan.FromMilliseconds(100)),
    };

    /// <summary>
    /// Materializes all matches in rule/application order (later rules overwrite earlier ones).
    /// A timeout propagates before any result is published; no partial span list escapes.
    /// The timeout is per regex match, not a deadline for the complete operation.
    /// </summary>
    /// <exception cref="RegexMatchTimeoutException">A rule exceeded its match budget.</exception>
    public static IReadOnlyList<HighlightSpan> ComputeSpans(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var spans = new List<HighlightSpan>();
        for (var ruleIndex = 0; ruleIndex < Rules.Length; ruleIndex++)
        {
            // MatchCollection is lazy: enumerate every rule before returning any spans.
            foreach (Match match in Rules[ruleIndex].Matches(text))
                spans.Add(new HighlightSpan(match.Index, match.Length, ruleIndex));
        }
        return spans.AsReadOnly();
    }
}
