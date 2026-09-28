using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Posts.CincinnatiCIFiber;

namespace OpenNest.Tests.CincinnatiCIFiber;

/// <summary>
/// Regression against the real machine program: posts the reconstructed
/// 12992-4SS nest (same part + layout as the sample NC) and compares contour
/// counts, macro counts and perimeter geometry with 12992-4SS_NEST.nc.
///
/// The fixture lives outside the repository (/srv/shared). Configure it in
/// OpenNest.Tests/test-config.json:
///   { "CIFiberSampleNcPath": "/srv/shared/.../12992-4SS_NEST.nc",
///     "CIFiberReconstructedNestPath": "/srv/shared/.../12992-4SS_NEST_reconstructed.nest" }
/// Both must exist for the test to run; otherwise it skips.
/// </summary>
public class CIFiberSampleRegressionTests
{
    private const int ExpectedParts = 109;
    private const int ExpectedContours = 2071;
    private const int ExpectedL2 = 1962;
    private const int ExpectedL4 = 109;

    private static (string Nc, string Nest)? ResolveFixture()
    {
        var nc = TestConfig.GetExistingPath("CIFiberSampleNcPath");
        var nest = TestConfig.GetExistingPath("CIFiberReconstructedNestPath");
        return nc != null && nest != null ? (nc, nest) : null;
    }

    private static Nest LoadAndLeadIn(string nestPath)
    {
        var reader = new NestReader(nestPath);
        var nest = reader.Read();

        var plate = nest.Plates[0];
        plate.CuttingParameters = new CuttingParameters
        {
            // The sample cuts clean holes with a short perpendicular linear
            // lead (pierce ~0.079 outside the contour), including the circles
            // (ArcCircleLeadIn). TF5200 13.2.4.1 requires a LINEAR first
            // motion block after G41/G42, so holes must have linear leads.
            InternalLeadIn = new LineLeadIn { Length = 0.075, ApproachAngle = 90 },
            ExternalLeadIn = new LineLeadIn { Length = 0.075, ApproachAngle = 90 },
            ArcCircleLeadIn = new LineLeadIn { Length = 0.075, ApproachAngle = 90 },
            PierceClearance = 0.02,
        };

        new LeadInAssigner { Sequencer = new LeftSideSequencer() }.Assign(plate);
        return nest;
    }

    private static string PostNest(Nest nest)
    {
        var post = new CIFiberPostProcessor(
            new CIFiberPostConfig { ConfigurationName = "CI FIBER 8K" }
        );
        using var ms = new MemoryStream();
        post.Post(nest, ms);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static Dictionary<string, int> CountMacroLines(string nc, string macro)
    {
        var needle = $"/L \"{macro}\"";
        return new Dictionary<string, int>
        {
            [macro] = Regex.Matches(nc, Regex.Escape(needle)).Count,
        };
    }

    private static List<(double X, double Y)> ParseG1Points(string nc)
    {
        var result = new List<(double, double)>();
        foreach (Match m in Regex.Matches(nc, @"G1X(-?[\d.]+)Y(-?[\d.]+)"))
        {
            result.Add(
                (
                    double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                    double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)
                )
            );
        }
        return result;
    }

    [SkippableFact]
    public void Post_ReconstructedNest_IsIdenticalAfterSavedLeadInsReload()
    {
        var fixture = ResolveFixture();
        Skip.If(fixture == null, "CI Fiber fixtures not configured in test-config.json");
        var nest = LoadAndLeadIn(fixture.Value.Nest);
        var expected = PostNest(nest);
        using var stream = new MemoryStream();
        new NestWriter(nest).Write(stream);
        stream.Position = 0;
        var reader = new NestReader(stream);
        var restored = reader.Read();

        Assert.Empty(reader.Warnings);
        Assert.All(restored.Plates.SelectMany(plate => plate.Parts), part => Assert.True(part.HasManualLeadIns));
        Assert.Equal(expected, PostNest(restored));
    }

    [SkippableFact]
    public void Post_ReconstructedNest_MatchesSampleCounts()
    {
        var fixture = ResolveFixture();
        Skip.If(fixture == null, "CI Fiber fixtures not configured in test-config.json");

        var nest = LoadAndLeadIn(fixture.Value.Nest);
        var output = PostNest(nest).Replace("\r\n", "\n");
        var sample = File.ReadAllText(fixture.Value.Nc).Replace("\r\n", "\n");

        // Parts
        var partCount = Regex.Matches(output, @"^\( Part #\d+ \)$", RegexOptions.Multiline).Count;
        Assert.Equal(ExpectedParts, partCount);

        // Contour labels (skip the N0: restart label)
        var labels = Regex
            .Matches(output, @"^N(\d+):$", RegexOptions.Multiline)
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToList();
        Assert.Equal(
            ExpectedContours,
            labels.Where(n => n != 0).Count()
        );
        Assert.Equal(
            Enumerable.Range(1, ExpectedContours),
            labels.Where(n => n != 0)
        );

        // Macro counts. Sample: L0 = contours + 1 tail; the posted file uses
        // the same one-per-contour + tail pattern.
        Assert.Equal(ExpectedContours + 1, CountMacroLines(output, "L0")["L0"]);
        Assert.Equal(ExpectedContours, CountMacroLines(output, "L6")["L6"]);
        Assert.Equal(ExpectedContours, CountMacroLines(output, "ZHSOFF")["ZHSOFF"]);

        // Interior/exterior split matches the sample: 109 exteriors (one
        // perimeter per part), the remaining 1962 contours interior.
        Assert.Equal(ExpectedL4, Regex.Matches(output, @"^/L ""L4""$", RegexOptions.Multiline).Count);
        Assert.Equal(ExpectedL2, Regex.Matches(output, @"^/L ""L2""$", RegexOptions.Multiline).Count);

        // Sample cross-check: same counts as the real machine program.
        Assert.Equal(
            ExpectedContours + 1,
            CountMacroLines(sample, "L0")["L0"]
        );
        Assert.Equal(
            ExpectedL2,
            CountMacroLines(sample, "L2")["L2"]
        );
        Assert.Equal(
            ExpectedL4,
            CountMacroLines(sample, "L4")["L4"]
        );
    }

    [SkippableFact]
    public void Post_ReconstructedNest_PerimeterGeometryMatchesSample()
    {
        var fixture = ResolveFixture();
        Skip.If(fixture == null, "CI Fiber fixtures not configured in test-config.json");

        var nest = LoadAndLeadIn(fixture.Value.Nest);
        var output = PostNest(nest).Replace("\r\n", "\n");
        var sample = File.ReadAllText(fixture.Value.Nc).Replace("\r\n", "\n");

        // Compare every exterior (L4) contour's CUT endpoints. Each perimeter
        // is the same closed loop; the lead-in/contour start point may differ
        // from the original CAM's, so per contour drop one occurrence of the
        // start point (the lead-in end, which the closed loop re-targets on
        // closure) and compare the remaining vertex multisets.
        var samplePts = ExtractExteriorContourCutPoints(sample);
        var postedPts = ExtractExteriorContourCutPoints(output);

        Assert.NotEmpty(samplePts);
        Assert.Equal(samplePts.Count, postedPts.Count);

        var sampleKeys = MultisetKeys(samplePts);
        var postedKeys = MultisetKeys(postedPts);
        Assert.Equal(sampleKeys.Count, postedKeys.Count);

        foreach (var (key, count) in sampleKeys)
            Assert.True(
                postedKeys.TryGetValue(key, out var postedCount) && postedCount == count,
                $"Vertex {key} x{count} missing from posted output (found {postedKeys.GetValueOrDefault(key)})"
            );
    }

    /// <summary>Round to 0.001 and count occurrences (tolerance-bucketed multiset).</summary>
    private static Dictionary<(long, long), int> MultisetKeys(List<(double X, double Y)> pts)
    {
        var keys = new Dictionary<(long, long), int>();
        foreach (var (x, y) in pts)
        {
            var key = (
                (long)System.Math.Round(x * 1000),
                (long)System.Math.Round(y * 1000)
            );
            keys[key] = keys.GetValueOrDefault(key) + 1;
        }
        return keys;
    }

    /// <summary>
    /// For each contour that used the L4 (exterior) lead-in macro, its cut
    /// endpoints (after /L "L6"), minus one occurrence of the contour start
    /// point (the lead-in target, which a closed loop re-hits on closure).
    /// The result is a rotation-invariant vertex multiset of every perimeter.
    /// </summary>
    private static List<(double X, double Y)> ExtractExteriorContourCutPoints(string nc)
    {
        var all = new List<(double, double)>();
        var inExterior = false;
        var cutOn = false;
        List<(double X, double Y)> current = null;
        (double X, double Y)? start = null;

        void FinishContour()
        {
            if (current != null && current.Count > 0)
            {
                var pts = current;
                if (start.HasValue)
                {
                    var idx = pts.FindIndex(
                        p =>
                            System.Math.Abs(p.X - start.Value.X) < 1e-4
                            && System.Math.Abs(p.Y - start.Value.Y) < 1e-4
                    );
                    if (idx >= 0)
                        pts.RemoveAt(idx);
                }
                all.AddRange(pts);
            }
            current = null;
            start = null;
        }

        (double, double)? ParsePoint(string line)
        {
            var m = Regex.Match(line, @"^G[0-3]X(-?[\d.]+)Y(-?[\d.]+)");
            if (!m.Success)
                return null;
            return (
                double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)
            );
        }

        foreach (var raw in nc.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (line == "/L \"L4\"")
            {
                FinishContour();
                inExterior = true;
                cutOn = false;
                current = new List<(double, double)>();
                continue;
            }
            if (line == "/L \"L2\"")
            {
                FinishContour();
                inExterior = false;
                cutOn = false;
                continue;
            }
            if (line == "/L \"L6\"")
            {
                cutOn = inExterior;
                continue;
            }
            if (line == "/L \"ZHSOFF\"")
            {
                FinishContour();
                inExterior = false;
                cutOn = false;
                continue;
            }

            if (!inExterior || current == null)
                continue;

            var pt = ParsePoint(line);
            if (pt == null)
                continue;

            if (cutOn)
                current.Add(pt.Value);
            else if (start == null)
                start = pt; // the linear lead-in move before L6
        }

        FinishContour();
        return all;
    }
}
