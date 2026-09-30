using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using OpenNest.CNC;

namespace OpenNest.Tests.CNC;

public class ProgramHighlightingTests
{
    [Theory]
    [InlineData("G00", 2, 3)]
    [InlineData("G000", -1, 0)]
    [InlineData("G00.5", 2, 3)]
    [InlineData("G00 X1", 2, 3)]
    [InlineData("G00X1", -1, 0)]
    [InlineData("G00_", -1, 0)]
    [InlineData("G00é", -1, 0)]
    [InlineData("G01", 3, 3)]
    [InlineData("G010", -1, 0)]
    [InlineData("G01.5", 3, 3)]
    [InlineData("G02 X1", 4, 3)]
    [InlineData("G03 X1", 4, 3)]
    [InlineData("G020", -1, 0)]
    [InlineData("G90", 1, 3)]
    [InlineData("G91", 1, 3)]
    [InlineData("G901", -1, 0)]
    [InlineData("G910", -1, 0)]
    [InlineData("G90.5", 1, 3)]
    [InlineData("g00 X1", -1, 0)]
    [InlineData(" G00 X1", -1, 0)]
    [InlineData(";)", 0, 2)]
    [InlineData("; G00 X1", 0, 8)]
    [InlineData("X1 ; comment", -1, 0)]
    [InlineData("G00 X1 ; comment", 2, 3)]
    public void SingleLineRetainsExistingAnchorsAndWordBoundaries(
        string text, int ruleIndex, int length)
    {
        var spans = ComputeSpans(text);
        if (ruleIndex < 0)
            Assert.Empty(spans);
        else
            Assert.Equal(new[] { new OracleSpan(0, length, ruleIndex) }, spans);
    }

    [Theory]
    [InlineData("\n", 0)]
    [InlineData("\r\n", 1)]
    public void MultilineCommentsRetainCarriageReturnAndCommandsOnlyColorTheirPrefix(
        string newline, int carriageReturnLength)
    {
        var lines = new[] { ";)", "G91", "G00 X1", "G01 Y2", "G02 X3", "G03 X4", "X1 ; mid-line" };
        var text = string.Join(newline, lines) + newline;
        var starts = new List<int>();
        var index = 0;
        foreach (var line in lines)
        {
            starts.Add(index);
            index += line.Length + newline.Length;
        }

        Assert.Equal(new[]
        {
            new OracleSpan(starts[0], 2 + carriageReturnLength, 0),
            new OracleSpan(starts[1], 3, 1),
            new OracleSpan(starts[2], 3, 2),
            new OracleSpan(starts[3], 3, 3),
            new OracleSpan(starts[4], 3, 4),
            new OracleSpan(starts[5], 3, 4),
        }, ComputeSpans(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n")]
    [InlineData("X1 Y2")]
    [InlineData("x\rG00")]
    public void EmptyOrUnmatchedTextHasNoSpans(string text) => Assert.Empty(ComputeSpans(text));

    [Fact]
    public void RulesStayInApplicationOrderRatherThanTextOrderAndNeverOverlap()
    {
        const string text = "G03\nG01\nG00\nG91\n;)\nG02\nG90\nG00";
        var spans = ComputeSpans(text);
        Assert.Equal(new[]
        {
            new OracleSpan(16, 2, 0),
            new OracleSpan(12, 3, 1),
            new OracleSpan(23, 3, 1),
            new OracleSpan(8, 3, 2),
            new OracleSpan(27, 3, 2),
            new OracleSpan(4, 3, 3),
            new OracleSpan(0, 3, 4),
            new OracleSpan(19, 3, 4),
        }, spans);
        Assert.Equal(spans.Select(s => s.RuleIndex).OrderBy(i => i), spans.Select(s => s.RuleIndex));
        var coloredIndices = spans.SelectMany(s => Enumerable.Range(s.Index, s.Length)).ToArray();
        Assert.Equal(coloredIndices.Length, coloredIndices.Distinct().Count());
    }

    // Frozen oracle: the original five inline rules from ProgramEditorControl.
    // Keep this independent of production patterns and timeout configuration.
    private static IReadOnlyList<OracleSpan> ComputeLegacySpans(string text)
    {
        var rules = new[]
        {
            new Regex(@"^;.*$", RegexOptions.Multiline),
            new Regex(@"^G9[01]\b", RegexOptions.Multiline),
            new Regex(@"^G00\b", RegexOptions.Multiline),
            new Regex(@"^G01\b", RegexOptions.Multiline),
            new Regex(@"^G0[23]\b", RegexOptions.Multiline),
        };
        var spans = new List<OracleSpan>();
        for (var ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
            foreach (Match match in rules[ruleIndex].Matches(text))
                spans.Add(new OracleSpan(match.Index, match.Length, ruleIndex));
        return spans;
    }

    [Fact]
    public void AllFiveCachedRulesHaveUnchangedPatternsAndFinite100MillisecondBudgets()
    {
        var rules = Assert.IsType<Regex[]>(typeof(ProgramHighlighting)
            .GetField("Rules", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
        Assert.Equal(new[] { @"^;.*$", @"^G9[01]\b", @"^G00\b", @"^G01\b", @"^G0[23]\b" },
            rules.Select(r => r.ToString()));
        Assert.All(rules, rule =>
        {
            Assert.Equal(RegexOptions.Multiline, rule.Options);
            Assert.NotEqual(Regex.InfiniteMatchTimeout, rule.MatchTimeout);
            Assert.Equal(TimeSpan.FromMilliseconds(100), rule.MatchTimeout);
        });
    }

    [Fact]
    public void GeneratedSizedFixtureRetainsEverySpanAndTheOriginalText()
    {
        var text = string.Concat(Enumerable.Repeat(
            "G91\r\n; Perimeter (CCW)\r\nG00 X1 Y2\r\nG01 X2 Y3\r\nG02 X1 Y0 I1 J2\r\nG03 X0 Y1 I2 J3\r\n", 5000));
        var original = text;
        var spans = ComputeSpans(text);
        Assert.Equal(30000, spans.Count);
        Assert.Equal(original, text);
    }

    [Fact]
    public void PublishedSpansAreReadOnlyAndIndependentOfSubsequentCalls()
    {
        var spans = ProgramHighlighting.ComputeSpans("G00");
        Assert.Throws<NotSupportedException>(() => ((IList<HighlightSpan>)spans)
            .Add(new HighlightSpan(0, 3, 4)));
        Assert.Empty(ProgramHighlighting.ComputeSpans(string.Empty));
        Assert.Equal(new[] { new HighlightSpan(0, 3, 2) }, spans);
    }

    private static IReadOnlyList<OracleSpan> ComputeSpans(string text)
    {
        var expected = ComputeLegacySpans(text);
        var actual = ProgramHighlighting.ComputeSpans(text)
            .Select(s => new OracleSpan(s.Index, s.Length, s.RuleIndex)).ToArray();
        Assert.Equal(expected, actual);
        return actual;
    }

    private readonly record struct OracleSpan(int Index, int Length, int RuleIndex);
}
