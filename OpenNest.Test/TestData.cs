using OpenNest.IO;

namespace OpenNest.Test;

public static class TestData
{
    private static readonly string? BasePath = ResolveBasePath();

    public static bool IsAvailable => BasePath != null;

    public static string SkipReason =>
        "Test data not found. Set OPENNEST_TEST_DATA env var or clone " +
        "https://git.thecozycat.net/aj/OpenNest.Test.git to ../OpenNest.Test.Data/";

    public static string GetPath(string filename)
    {
        if (BasePath == null)
            throw new InvalidOperationException(SkipReason);

        var path = Path.Combine(BasePath, filename);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Test fixture not found: {path}");

        return path;
    }

    public static Nest LoadNest(string filename)
    {
        var reader = new NestReader(GetPath(filename));
        return reader.Read();
    }

    public static Plate CleanPlateFrom(Plate reference)
    {
        var plate = new Plate();
        plate.Size = reference.Size;
        plate.PartSpacing = reference.PartSpacing;
        plate.EdgeSpacing = reference.EdgeSpacing;
        plate.Quadrant = reference.Quadrant;
        return plate;
    }

    private static string? ResolveBasePath()
    {
        // 1. Environment variable
        var envPath = Environment.GetEnvironmentVariable("OPENNEST_TEST_DATA");

        if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
            return envPath;

        // 2. Sibling directory (../OpenNest.Test.Data/ relative to solution root)
        var dir = AppContext.BaseDirectory;

        // Walk up from bin/Debug/net8.0-windows to find the solution root.
        for (var i = 0; i < 6; i++)
        {
            var parent = Directory.GetParent(dir);

            if (parent == null)
                break;

            dir = parent.FullName;
            var candidate = Path.Combine(dir, "OpenNest.Test.Data");

            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
