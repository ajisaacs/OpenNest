using System.Reflection;

namespace OpenNest.FrontEnd.Tests;

/// <summary>Runs the console front end in-process. Import with <c>using static</c>.</summary>
internal static class ConsoleHost
{
    /// <summary>Calls <c>NestConsole.Run</c> and returns its exit code.</summary>
    public static int RunConsole(params string[] args)
    {
        var method = Assembly.Load("OpenNest.Console").GetType("NestConsole")!
            .GetMethod("Run", BindingFlags.Static | BindingFlags.Public)!;
        return (int)method.Invoke(null, new object[] { args })!;
    }
}
