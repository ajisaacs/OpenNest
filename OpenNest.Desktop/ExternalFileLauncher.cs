using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenNest.Desktop;

/// <summary>
/// Opens a file in the program the system associates with its type: Plate &gt; View in CAD's
/// DXF, View Nest Report's PDF. Windows opens it through the shell, macOS through
/// <c>open</c> and Linux and other Unix desktops through <c>xdg-open</c>.
/// </summary>
public static class ExternalFileLauncher
{
    /// <summary>The start info that opens <paramref name="path"/> on the running platform.</summary>
    public static ProcessStartInfo StartInfo(string path) => StartInfo(path, CurrentPlatform());

    /// <summary>The start info that opens <paramref name="path"/> on <paramref name="platform"/>.</summary>
    public static ProcessStartInfo StartInfo(string path, OSPlatform platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // .NET defaults UseShellExecute to false, which would try to run the file itself as a
        // program; the shell opens it in the associated program instead.
        if (platform == OSPlatform.Windows)
            return new ProcessStartInfo(path) { UseShellExecute = true };

        var info = new ProcessStartInfo(platform == OSPlatform.OSX ? "open" : "xdg-open")
        {
            UseShellExecute = false,
        };
        // An argument, never a shell command line: spaces and quotes in the path stay literal.
        info.ArgumentList.Add(path);
        return info;
    }

    /// <summary>Opens <paramref name="path"/> in its associated program; throws when it cannot start.</summary>
    public static void Open(string path)
    {
        using var process = Process.Start(StartInfo(path));
    }

    private static OSPlatform CurrentPlatform() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatform.Windows
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatform.OSX
        : OSPlatform.Linux;
}
