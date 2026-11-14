using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

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

    /// <summary>
    /// Opens <paramref name="path"/> and, where an opener program reports the result
    /// (<c>xdg-open</c>, <c>open</c>), waits for it without blocking and returns why it failed,
    /// or null once it opened. The Windows shell reports a failure by throwing instead. Throws
    /// when the opener cannot start.
    /// </summary>
    public static async Task<string> OpenAsync(string path)
    {
        var info = StartInfo(path);
        using var process = Process.Start(info);
        if (info.UseShellExecute || process == null)
            return null;
        await process.WaitForExitAsync().ConfigureAwait(false);
        return FailureMessage(process.ExitCode, path);
    }

    /// <summary>
    /// What an <c>xdg-open</c> or <c>open</c> exit code means for <paramref name="path"/>, or
    /// null for success (0).
    /// </summary>
    public static string FailureMessage(int exitCode, string path) => exitCode switch
    {
        0 => null,
        2 => $"'{path}' was not found.",
        3 => $"No program is set up to open '{System.IO.Path.GetFileName(path)}'.",
        _ => $"The system could not open '{path}' (exit code {exitCode}).",
    };

    private static OSPlatform CurrentPlatform() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatform.Windows
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatform.OSX
        : OSPlatform.Linux;
}
