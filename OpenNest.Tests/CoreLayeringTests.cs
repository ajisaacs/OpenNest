using System.Text.RegularExpressions;

namespace OpenNest.Tests;

/// <summary>
/// <c>OpenNest.Math</c> and <c>OpenNest.Geometry</c> are the bottom layers of OpenNest.Core: Math
/// uses nothing else in OpenNest, and Geometry uses only Math plus the root-namespace types in
/// <see cref="RootTypesGeometryMayUse"/>. Namespaces inside one assembly are not checked by the
/// compiler, so this scans the sources against the type names reflection finds in Core.
/// <para>
/// It is a token scan, not a binder: it catches a higher type being named, which is how every
/// dependency so far has arrived. It cannot see a higher type reached without naming it (an
/// extension method declared in the root <c>OpenNest</c> namespace, which child namespaces see
/// without a using).
/// </para>
/// </summary>
public class CoreLayeringTests
{
    private const string MathNamespace = "OpenNest.Math";
    private const string GeometryNamespace = "OpenNest.Geometry";

    /// <summary>
    /// Root-namespace (<c>OpenNest</c>) types Geometry is allowed to name. They stay in the root
    /// namespace because moving them would rename public types external plug-ins bind to;
    /// <see cref="AllowlistedRootTypes_ReferenceOnlyMathAndGeometry"/> keeps them from pulling a
    /// higher layer in behind Geometry's back. Do not grow this list to make a failure go away:
    /// move the offending code up a layer instead.
    /// </summary>
    private static readonly string[] RootTypesGeometryMayUse =
    [
        "OffsetSide",
        "RotationType",
        "PushDirection",
        "RelativePosition",
        "PerfCounters",
        "SpecialLayers",
    ];

    /// <summary>
    /// Higher-layer type names the scan cannot tell from something else of the same name, each
    /// with the reason. Names that Math/Geometry declare themselves are not listed here: inside
    /// those namespaces C# binds the simple name to the nearer declaration, so the scan drops
    /// them on its own (see <see cref="HigherTypeNames"/>).
    /// </summary>
    private static readonly Dictionary<string, string> AmbiguousNames = new()
    {
        // SpecialLayers.Tab is a Layer field. The Tab class lives in OpenNest.CNC.CuttingStrategy,
        // which no scanned file can reach without a using the namespace rule already reports.
        ["Tab"] = "SpecialLayers.Tab field",
    };

    private static readonly Regex NamespaceReference = new(@"(?<![\w.])OpenNest\s*\.\s*([A-Za-z_]\w*)");
    private static readonly Regex Identifier = new(@"(?<![\w@])[A-Za-z_]\w*");

    /// <summary>First segments of Core's namespaces under <c>OpenNest</c>: Math, Geometry, CNC...</summary>
    private static readonly HashSet<string> ChildNamespaces = typeof(Plate).Assembly.GetTypes()
        .Select(type => type.Namespace)
        .Where(name => name != null && name.StartsWith("OpenNest.", StringComparison.Ordinal))
        .Select(name => name!.Split('.')[1])
        .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Math_ReferencesNothingElseInOpenNest()
    {
        var higher = HigherTypeNames(lowerNamespaces: [MathNamespace], shadowingNamespace: MathNamespace);

        var violations = SourceFiles("Math")
            .SelectMany(file => Violations(file, allowedNamespaces: ["Math"], higher))
            .ToList();

        Assert.True(violations.Count == 0, Report("OpenNest.Math must not reference the rest of OpenNest", violations));
    }

    [Fact]
    public void Geometry_ReferencesOnlyMathAndAllowlistedRootTypes()
    {
        var higher = HigherTypeNames(lowerNamespaces: [MathNamespace, GeometryNamespace], shadowingNamespace: GeometryNamespace);
        higher.ExceptWith(RootTypesGeometryMayUse);

        var violations = SourceFiles("Geometry")
            .SelectMany(file => Violations(file, allowedNamespaces: ["Math", "Geometry"], higher))
            .ToList();

        Assert.True(violations.Count == 0, Report("OpenNest.Geometry may reference only OpenNest.Math and the allowlisted root types", violations));
    }

    [Fact]
    public void AllowlistedRootTypes_ReferenceOnlyMathAndGeometry()
    {
        // These files sit in the root namespace, where a root type wins over a same-named
        // Geometry type, so nothing is shadowed here.
        var higher = HigherTypeNames(lowerNamespaces: [MathNamespace, GeometryNamespace], shadowingNamespace: null);
        higher.ExceptWith(RootTypesGeometryMayUse);

        var coreDirectory = CoreDirectory();
        var rootFiles = Directory.GetFiles(coreDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(path => (Path: path, Code: StripCommentsAndLiterals(File.ReadAllText(path))))
            .ToList();

        var violations = new List<string>();
        foreach (var name in RootTypesGeometryMayUse)
        {
            var type = typeof(Plate).Assembly.GetType("OpenNest." + name);
            Assert.True(type != null, $"Allowlisted type OpenNest.{name} no longer exists in OpenNest.Core; remove it from the allowlist.");

            var declaration = new Regex(@"\b(?:enum|class|struct|interface|record)\s+" + Regex.Escape(name) + @"\b");
            var declaring = rootFiles.Where(file => declaration.IsMatch(file.Code)).ToList();
            Assert.True(declaring.Count == 1, $"Expected exactly one file in OpenNest.Core's root declaring {name}, found {declaring.Count}.");

            violations.AddRange(Violations(declaring[0].Path, allowedNamespaces: ["Math", "Geometry"], higher));
        }

        Assert.True(violations.Count == 0, Report("Types Geometry is allowed to use may reference only OpenNest.Math and OpenNest.Geometry", violations));
    }

    [Fact]
    public void AmbiguousNames_AreStillAmbiguous()
    {
        // An exclusion that no longer names a higher Core type is dead weight hiding nothing.
        var higher = HigherTypeNames(lowerNamespaces: [MathNamespace, GeometryNamespace], shadowingNamespace: null, applyExclusions: false);

        Assert.Empty(AmbiguousNames.Keys.Where(name => !higher.Contains(name)));
    }

    /// <summary>
    /// Simple names of Core's top-level types outside <paramref name="lowerNamespaces"/>. Nested
    /// types are left out: they are only reachable through their outer type, which is in the set.
    /// Names that <paramref name="shadowingNamespace"/> also declares are left out too, because
    /// code in that namespace binds the simple name to its own type before any outer namespace.
    /// </summary>
    private static HashSet<string> HigherTypeNames(string[] lowerNamespaces, string? shadowingNamespace, bool applyExclusions = true)
    {
        var types = typeof(Plate).Assembly.GetTypes()
            .Where(type => !type.IsNested && type.Namespace != null && !type.Name.Contains('<'))
            .ToList();

        var higher = types
            .Where(type => !lowerNamespaces.Contains(type.Namespace))
            .Select(SimpleName)
            .ToHashSet(StringComparer.Ordinal);

        if (shadowingNamespace != null)
            higher.ExceptWith(types.Where(type => type.Namespace == shadowingNamespace).Select(SimpleName));

        if (applyExclusions)
            higher.ExceptWith(AmbiguousNames.Keys);

        return higher;
    }

    private static string SimpleName(Type type)
    {
        var tick = type.Name.IndexOf('`');
        return tick < 0 ? type.Name : type.Name[..tick];
    }

    private static IEnumerable<string> Violations(string path, string[] allowedNamespaces, HashSet<string> higherTypeNames)
    {
        var code = StripCommentsAndLiterals(File.ReadAllText(path));
        var file = Path.GetRelativePath(RepositoryRoot(), path).Replace('\\', '/');

        foreach (Match match in NamespaceReference.Matches(code))
        {
            var name = match.Groups[1].Value;
            if (!allowedNamespaces.Contains(name))
                yield return $"{file}:{LineOf(code, match.Index)}: OpenNest.{name}";
        }

        foreach (Match match in Identifier.Matches(code))
        {
            // A name after a dot is a member (entity.Layer) or was qualified, and a qualified
            // OpenNest.X is already reported above.
            if (FollowsDot(code, match.Index))
                continue;

            if (higherTypeNames.Contains(match.Value))
                yield return $"{file}:{LineOf(code, match.Index)}: {match.Value}";

            // Code inside OpenNest.* can also qualify from a sibling namespace: CNC.Program.
            else if (ChildNamespaces.Contains(match.Value) && !allowedNamespaces.Contains(match.Value)
                && PrecedesDot(code, match.Index + match.Length))
                yield return $"{file}:{LineOf(code, match.Index)}: {match.Value}. (namespace OpenNest.{match.Value})";
        }
    }

    private static bool FollowsDot(string code, int index)
    {
        var i = index - 1;
        while (i >= 0 && char.IsWhiteSpace(code[i]))
            i--;

        return i >= 0 && code[i] == '.';
    }

    private static bool PrecedesDot(string code, int index)
    {
        while (index < code.Length && char.IsWhiteSpace(code[index]))
            index++;

        return index < code.Length && code[index] == '.';
    }

    private static int LineOf(string code, int index)
    {
        var line = 1;
        for (var i = 0; i < index; i++)
            if (code[i] == '\n')
                line++;

        return line;
    }

    private static string Report(string rule, List<string> violations) =>
        $"{rule}. Offending references (file:line: identifier):{Environment.NewLine}{string.Join(Environment.NewLine, violations)}";

    private static IEnumerable<string> SourceFiles(string coreSubdirectory)
    {
        var directory = Path.Combine(CoreDirectory(), coreSubdirectory);
        var files = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);

        Assert.NotEmpty(files);
        return files.OrderBy(path => path, StringComparer.Ordinal);
    }

    private static string CoreDirectory() => Path.Combine(RepositoryRoot(), "OpenNest.Core");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OpenNest.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory.FullName;
    }

    /// <summary>
    /// Blanks comments, string literals and char literals with spaces, keeping every offset and
    /// line break so matches report true line numbers. Interpolation holes of ordinary and
    /// verbatim strings are code and are kept; raw string literals are blanked whole.
    /// </summary>
    internal static string StripCommentsAndLiterals(string source)
    {
        var output = source.ToCharArray();
        var position = 0;
        ScanCode(source, output, ref position, untilClosingBrace: false);
        return new string(output);
    }

    private static void ScanCode(string source, char[] output, ref int i, bool untilClosingBrace)
    {
        var depth = 0;
        while (i < source.Length)
        {
            var c = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (c == '/' && next == '/')
            {
                while (i < source.Length && source[i] != '\n')
                    Blank(source, output, i++);
            }
            else if (c == '/' && next == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 2;
                while (i < end)
                    Blank(source, output, i++);
            }
            else if (c == '\'')
            {
                Blank(source, output, i++);
                while (i < source.Length && source[i] != '\'')
                {
                    if (source[i] == '\\')
                        Blank(source, output, i++);
                    if (i < source.Length)
                        Blank(source, output, i++);
                }

                if (i < source.Length)
                    Blank(source, output, i++);
            }
            else if (c == '"' || ((c == '$' || c == '@') && StartsString(source, i)))
            {
                ScanString(source, output, ref i);
            }
            else if (untilClosingBrace && c == '{')
            {
                depth++;
                i++;
            }
            else if (untilClosingBrace && c == '}')
            {
                if (depth == 0)
                    return;

                depth--;
                i++;
            }
            else
            {
                i++;
            }
        }
    }

    private static bool StartsString(string source, int i)
    {
        while (i < source.Length && (source[i] == '$' || source[i] == '@'))
            i++;

        return i < source.Length && source[i] == '"';
    }

    private static void ScanString(string source, char[] output, ref int i)
    {
        var interpolated = false;
        var verbatim = false;
        for (; source[i] != '"'; i++)
        {
            interpolated |= source[i] == '$';
            verbatim |= source[i] == '@';
            Blank(source, output, i);
        }

        var quotes = 0;
        while (i + quotes < source.Length && source[i + quotes] == '"')
            quotes++;

        if (!verbatim && quotes >= 3)
        {
            var end = source.IndexOf(new string('"', quotes), i + quotes, StringComparison.Ordinal);
            end = end < 0 ? source.Length : end + quotes;
            while (end < source.Length && source[end] == '"')
                end++;
            while (i < end)
                Blank(source, output, i++);
            return;
        }

        Blank(source, output, i++);
        while (i < source.Length)
        {
            var c = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (verbatim && c == '"' && next == '"')
            {
                Blank(source, output, i++);
                Blank(source, output, i++);
            }
            else if (c == '"')
            {
                Blank(source, output, i++);
                return;
            }
            else if (!verbatim && c == '\\')
            {
                Blank(source, output, i++);
                if (i < source.Length)
                    Blank(source, output, i++);
            }
            else if (interpolated && (c == '{' || c == '}') && next == c)
            {
                Blank(source, output, i++);
                Blank(source, output, i++);
            }
            else if (interpolated && c == '{')
            {
                Blank(source, output, i++);
                ScanCode(source, output, ref i, untilClosingBrace: true);
                if (i < source.Length)
                    Blank(source, output, i++);
            }
            else
            {
                Blank(source, output, i++);
            }
        }
    }

    private static void Blank(string source, char[] output, int i)
    {
        if (source[i] != '\n' && source[i] != '\r')
            output[i] = ' ';
    }
}
