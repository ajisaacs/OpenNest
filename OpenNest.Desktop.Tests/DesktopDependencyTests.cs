using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

namespace OpenNest.Desktop.Tests;

/// <summary>
/// OpenNest.Desktop is shared by the WinForms and Avalonia front ends, so it must stay free of
/// any UI toolkit: no WinForms/WindowsDesktop framework, System.Drawing.Common or Avalonia.
/// </summary>
public class DesktopDependencyTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "System.Windows.Forms",
        "Microsoft.WindowsDesktop",
        "WindowsBase",
        "PresentationCore",
        "PresentationFramework",
        "System.Drawing.Common",
        "Avalonia",
    ];

    [Fact]
    public void DesktopAssembly_ReferencesNoUiToolkit()
    {
        var assembly = Assembly.Load(new AssemblyName("OpenNest.Desktop"));
        var references = assembly.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.Empty(references.Where(IsForbidden));
    }

    [Fact]
    public void DesktopProject_TargetsPlainNet8WithoutUiFrameworks()
    {
        var project = XDocument.Load(Path.Combine(DesktopProjectDirectory(), "OpenNest.Desktop.csproj"));
        var elements = project.Descendants().ToList();

        var targets = elements
            .Where(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks")
            .SelectMany(e => e.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
        Assert.Equal(["net8.0"], targets);

        Assert.DoesNotContain(elements, e =>
            e.Name.LocalName is "UseWindowsForms" or "UseWPF" or "UseWindowsDesktop"
            && e.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(elements, e => e.Name.LocalName == "FrameworkReference");

        var packages = elements
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty);
        Assert.Empty(packages.Where(IsForbidden));
    }

    [Fact]
    public void DesktopProject_RestoresNoUiToolkitPackagesTransitively()
    {
        var assetsPath = Path.Combine(DesktopProjectDirectory(), "obj", "project.assets.json");
        Assert.True(File.Exists(assetsPath), $"Restore output not found: {assetsPath}");

        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var libraries = assets.RootElement.GetProperty("libraries").EnumerateObject()
            .Select(library => library.Name.Split('/')[0]);

        Assert.Empty(libraries.Where(IsForbidden));
    }

    private static bool IsForbidden(string name) =>
        ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static string DesktopProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OpenNest.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "OpenNest.Desktop");
    }
}
