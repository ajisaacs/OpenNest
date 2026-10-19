using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OpenNest.Geometry;
using OpenNest.Posts.Cincinnati;

namespace OpenNest.Tests.Cincinnati;

/// <summary>
/// Posts nests with the Cincinnati CL post and replays the G-code to see where the
/// machine actually cuts.
/// </summary>
internal static class CincinnatiReplay
{
    /// <summary>A config whose output is easy to replay: no anti-dive, no pallet exchange.</summary>
    internal static CincinnatiPostConfig Config(bool partSubprograms) => new()
    {
        PostedAccuracy = 4,
        UsePartSubprograms = partSubprograms,
        UseAntiDive = false,
        PalletExchange = PalletMode.None,
    };

    internal static string Post(Nest nest, bool partSubprograms) => Post(nest, Config(partSubprograms));

    internal static string Post(Nest nest, CincinnatiPostConfig config)
    {
        using var ms = new MemoryStream();
        new CincinnatiPostProcessor(config).Post(nest, ms);
        return Encoding.UTF8.GetString(ms.ToArray()).Replace("\r", "");
    }

    internal sealed class Result
    {
        public List<(Vector Center, double Radius)> FullCircles { get; } = new();
        public int Pierces { get; set; }
        public int EmptyPierces { get; set; }

        /// <summary>Every pierce with the head raise posted before it (null for none).</summary>
        public List<(Vector Point, string? Raise)> PierceLog { get; } = new();

        /// <summary>Every cutting move, start to end, in machine coordinates.</summary>
        public List<(Vector From, Vector To)> Cuts { get; } = new();

        /// <summary>Head raises posted while the head was already raised.</summary>
        public int DoubleRaises { get; set; }

        /// <summary>Rapids taken after a cut with no head raise since.</summary>
        public int HeadDownRapids { get; set; }
    }

    /// <summary>
    /// Minimal replay of the post's own dialect: M98/M99 calls, G52/G92 frames,
    /// G0-G3 under G90 with incremental I/J, and M47 head raises (a block-deleted
    /// /M47 counts as a raise). Every arc must end on its own radius.
    /// </summary>
    internal static Result Run(string output)
    {
        var lines = output.Split('\n').Select(Clean).ToList();
        var labels = new Dictionary<int, int>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith(':'))
                labels[int.Parse(lines[i][1..], CultureInfo.InvariantCulture)] = i;
        }

        var run = new Result();
        var position = Vector.Zero; // machine position
        var g92 = Vector.Zero; // work origin in machine frame
        var g52 = Vector.Zero; // local shift in the work frame
        var returns = new Stack<int>();
        var pierced = false;
        var cutSincePierce = false;
        string? raise = null; // raise posted since the last pierce
        var headDown = false;
        var pc = 0;

        while (pc < lines.Count)
        {
            var line = lines[pc++];
            if (line.Length == 0 || line.StartsWith(':') || line.StartsWith("GOTO", System.StringComparison.Ordinal))
                continue;
            if (line.StartsWith("#", System.StringComparison.Ordinal))
                continue;

            var blockDelete = line.StartsWith('/');
            if (blockDelete)
                line = line[1..];

            var words = Words(line);
            if (words.TryGetValue("M", out var m))
            {
                if (m == "30")
                    break;
                if (m == "98")
                {
                    returns.Push(pc);
                    pc = labels[int.Parse(words["P"], CultureInfo.InvariantCulture)] + 1;
                    continue;
                }
                if (m == "99")
                {
                    pc = returns.Pop();
                    continue;
                }
                if (m is "35" or "135")
                {
                    if (pierced && !cutSincePierce)
                        run.EmptyPierces++;
                    pierced = false;
                }
                if (m == "47")
                {
                    if (raise != null)
                        run.DoubleRaises++;
                    raise = blockDelete ? "/" + line : line;
                    headDown = false;
                }
                if (m == "42")
                    headDown = false;
                continue;
            }

            if (!words.TryGetValue("G", out var g))
                continue;

            switch (g)
            {
                case "84":
                case "85":
                    run.Pierces++;
                    run.PierceLog.Add((position, raise));
                    raise = null;
                    pierced = true;
                    headDown = true;
                    cutSincePierce = false;
                    break;
                case "52":
                    g52 = new Vector(Num(words["X"]), Num(words["Y"]));
                    break;
                case "92":
                    g92 = words["X"].StartsWith('#')
                        ? Vector.Zero
                        : position - g52 - new Vector(Num(words["X"]), Num(words["Y"]));
                    break;
                case "0":
                case "1":
                case "2":
                case "3":
                    {
                        var target = g92 + g52 + new Vector(Num(words["X"]), Num(words["Y"]));
                        if (g is "2" or "3")
                        {
                            var center = position + new Vector(Num(words["I"]), Num(words["J"]));
                            var radius = position.DistanceTo(center);
                            Assert.True(
                                System.Math.Abs(target.DistanceTo(center) - radius) < 1e-3,
                                $"Arc ends off its radius: {line}"
                            );
                            if (target.DistanceTo(position) < 1e-6)
                                run.FullCircles.Add((center, radius));
                        }
                        if (g == "0" && headDown)
                            run.HeadDownRapids++;
                        if (g != "0")
                        {
                            cutSincePierce = true;
                            if (pierced)
                                run.Cuts.Add((position, target));
                        }
                        position = target;
                        break;
                    }
            }
        }

        return run;
    }

    internal static bool Near(Vector a, Vector b) => a.DistanceTo(b) < 1e-3;

    private static string Clean(string line)
    {
        var text = line.Trim();
        var comment = text.IndexOf('(');
        if (comment >= 0)
            text = text[..comment].Trim();
        if (text.StartsWith('N'))
        {
            var space = text.IndexOf(' ');
            text = space < 0 ? "" : text[(space + 1)..];
        }
        return text;
    }

    // Words in a block, keyed by letter; a leading G41/G42 gives way to the motion code after it.
    private static Dictionary<string, string> Words(string line)
    {
        var words = new Dictionary<string, string>();
        foreach (var word in line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
            words[word[..1]] = word[1..];
        return words;
    }

    private static double Num(string value) => double.Parse(value, CultureInfo.InvariantCulture);
}
