using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using OpenNest.Mcp.Tools;

namespace OpenNest.FrontEnd.Tests;

public class McpEngineHarnessProcessTests(McpEngineHarnessChildFixture fixture)
    : IClassFixture<McpEngineHarnessChildFixture>
{
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Normal_ReachesPrivateChild()
    {
        var root = fixture.NewRun();
        var result = await fixture.Tool().TestEngine(root).WaitAsync(Watchdog);
        await McpEngineHarnessChildFixture.Ready(root);
        Assert.Equal(JsonSerializer.Serialize(fixture.Arguments(root)), result);
    }

    [Fact]
    public async Task Completed_PreservesStderrAndNonzeroStatus()
    {
        var root = fixture.NewRun();
        var result = await fixture.Tool().TestEngine(root, "nonzero").WaitAsync(Watchdog);
        Assert.Equal(JsonSerializer.Serialize(fixture.Arguments(root, "nonzero"))
            + Environment.NewLine + Environment.NewLine + "=== Errors ===" + Environment.NewLine
            + "fixture stderr" + Environment.NewLine + "Process exited with code 7" + Environment.NewLine, result);
    }

    [Fact]
    public async Task Arguments_PreserveSpacesAndQuotes()
    {
        var root = fixture.NewRun();
        const string drawing = "drawing with \"quotes\" and trailing\\";
        var output = Path.Combine(fixture.Directory, "output with spaces.nest");
        var result = await fixture.Tool().TestEngine(root, drawing, 42, output).WaitAsync(Watchdog);
        Assert.Equal(fixture.Arguments(root, drawing, 42, output), JsonSerializer.Deserialize<string[]>(result));
    }

    [Fact]
    public async Task ConcurrentDrains_PreserveFullOutputBeyondPipeCapacity()
    {
        var root = fixture.NewRun();
        var result = await fixture.Tool().TestEngine(root, "flood").WaitAsync(Watchdog);
        Assert.Equal(JsonSerializer.Serialize(fixture.Arguments(root, "flood")) + Environment.NewLine
            + new string('O', 256 * 1024) + Environment.NewLine + Environment.NewLine
            + "=== Errors ===" + Environment.NewLine + new string('E', 256 * 1024), result);
    }

    [Fact]
    public async Task Deadline_BoundsHungChildAndReapsIt()
    {
        var root = fixture.NewRun();
        var run = fixture.Tool(5).TestEngine(root, "wait");
        Process? parent = null;
        try
        {
            await McpEngineHarnessChildFixture.Ready(root);
            parent = McpEngineHarnessChildFixture.GetProcess(root, "parent");
            var result = await run.WaitAsync(Watchdog);
            Assert.Contains("timed out", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Cleanup failed", result);
            await parent.WaitForExitAsync().WaitAsync(Watchdog);
        }
        finally
        {
            McpEngineHarnessChildFixture.Release(root);
            McpEngineHarnessChildFixture.Stop(parent);
            await run.WaitAsync(Watchdog);
            parent?.Dispose();
        }
    }

    [Fact]
    public async Task Cancellation_StopsOwnedLiveTreeWithoutKillingUnrelatedProcess()
    {
        using var cancellation = new CancellationTokenSource();
        var root = fixture.NewRun();
        var unrelated = fixture.NewRun();
        using var independent = Process.Start(fixture.StartInfo(unrelated, "wait"))!;
        var run = fixture.Tool().TestEngine(root, "tree", cancellationToken: cancellation.Token);
        Process? parent = null;
        Process? descendant = null;
        try
        {
            await McpEngineHarnessChildFixture.Ready(root);
            await McpEngineHarnessChildFixture.Ready(unrelated);
            parent = McpEngineHarnessChildFixture.GetProcess(root, "parent");
            descendant = McpEngineHarnessChildFixture.GetProcess(root, "child");
            await cancellation.CancelAsync();
            var result = await run.WaitAsync(Watchdog);
            Assert.Contains("cancelled", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Cleanup failed", result);
            await parent.WaitForExitAsync().WaitAsync(Watchdog);
            await descendant.WaitForExitAsync().WaitAsync(Watchdog);
            Assert.False(independent.HasExited);
        }
        finally
        {
            McpEngineHarnessChildFixture.Release(root);
            McpEngineHarnessChildFixture.Release(unrelated);
            McpEngineHarnessChildFixture.Stop(parent);
            McpEngineHarnessChildFixture.Stop(descendant);
            McpEngineHarnessChildFixture.Stop(independent);
            await run.WaitAsync(Watchdog);
            parent?.Dispose();
            descendant?.Dispose();
        }
    }

    [Fact]
    public async Task Deadline_BoundsPipeDrainAfterParentExits()
    {
        var root = fixture.NewRun();
        var run = fixture.Tool(5).TestEngine(root, "orphan");
        Process? parent = null;
        Process? descendant = null;
        try
        {
            await McpEngineHarnessChildFixture.Ready(root);
            parent = McpEngineHarnessChildFixture.GetProcess(root, "parent");
            descendant = McpEngineHarnessChildFixture.GetProcess(root, "child");
            McpEngineHarnessChildFixture.ExitParent(root);
            await parent.WaitForExitAsync().WaitAsync(Watchdog);
            Assert.False(descendant.HasExited);
            var result = await run.WaitAsync(Watchdog);
            Assert.Contains("timed out", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Cleanup failed", result);
        }
        finally
        {
            // A descendant detached after parent exit cannot be identified by Process.Kill(tree).
            McpEngineHarnessChildFixture.Release(root);
            McpEngineHarnessChildFixture.Stop(parent);
            McpEngineHarnessChildFixture.Stop(descendant);
            await run.WaitAsync(Watchdog);
            parent?.Dispose();
            descendant?.Dispose();
        }
    }

    [Fact]
    public async Task AlreadyCancelled_DoesNotStartChild()
    {
        var root = fixture.NewRun();
        var result = await fixture.Tool().TestEngine(root,
            cancellationToken: new CancellationToken(true)).WaitAsync(Watchdog);
        Assert.Contains("cancelled", result, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(root + ".parent"));
    }

    [Theory]
    [InlineData("EngineHarness:SourceRoot", "relative checkout")]
    [InlineData("EngineHarness:DotnetPath", "dotnet")]
    [InlineData("EngineHarness:TimeoutSeconds", "0")]
    [InlineData("EngineHarness:TimeoutSeconds", "2147484")]
    [InlineData("EngineHarness:TimeoutSeconds", "invalid")]
    public async Task InvalidConfiguration_FailsClearlyWithoutStartingChild(string key, string value)
    {
        var root = fixture.NewRun();
        var result = await fixture.Tool(overrideKey: key, overrideValue: value).TestEngine(root).WaitAsync(Watchdog);
        Assert.Contains(key, result);
        Assert.False(File.Exists(root + ".parent"));
    }

    [Fact]
    public async Task MissingNest_FailsClearly()
    {
        Assert.Contains("nest file not found", await fixture.Tool().TestEngine("missing.nest"));
    }
}

public sealed class McpEngineHarnessChildFixture : IAsyncLifetime
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "opennest-mcp-harness-" + Guid.NewGuid());
    public string Bin => Path.Combine(Directory, "bin");
    public string Host => Path.Combine(Bin, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
    public string SourceRoot => Path.Combine(Directory, "private checkout with spaces");

    public async Task InitializeAsync()
    {
        System.IO.Directory.CreateDirectory(Directory);
        System.IO.Directory.CreateDirectory(Path.Combine(SourceRoot, "OpenNest.Console"));
        await File.WriteAllTextAsync(Path.Combine(SourceRoot, "OpenNest.Console", "OpenNest.Console.csproj"), "<Project />");
        var project = Path.Combine(Directory, "HarnessChild.csproj");
        await File.WriteAllTextAsync(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <EnableDefaultItems>false</EnableDefaultItems>
              </PropertyGroup>
              <ItemGroup><Compile Include="Program.cs" /></ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(Directory, "Program.cs"), ChildSource);
        var runtime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var dotnet = Path.GetFullPath(Path.Combine(runtime, "..", "..", "..",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var info = new ProcessStartInfo(dotnet)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "build", project, "--output", Bin, "--nologo" })
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(120));
            Assert.True(process.ExitCode == 0, await stdout + await stderr);
        }
        finally
        {
            Stop(process);
        }
        File.Copy(Path.Combine(Bin, OperatingSystem.IsWindows() ? "HarnessChild.exe" : "HarnessChild"), Host);
    }

    public Task DisposeAsync()
    {
        System.IO.Directory.Delete(Directory, true);
        return Task.CompletedTask;
    }

    public string NewRun()
    {
        var path = Path.Combine(Directory, "run " + Guid.NewGuid() + ".nest");
        File.WriteAllText(path, "process control fixture, not a real nest");
        return path;
    }

    public TestTools Tool(int timeoutSeconds = 120, string? overrideKey = null, string? overrideValue = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["EngineHarness:SourceRoot"] = SourceRoot,
            ["EngineHarness:DotnetPath"] = Host,
            ["EngineHarness:TimeoutSeconds"] = timeoutSeconds.ToString(),
        };
        if (overrideKey != null) values[overrideKey] = overrideValue;
        return new TestTools(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    public string[] Arguments(string run, string? drawing = null, int plateIndex = 0, string? output = null)
    {
        var arguments = new List<string>
        {
            "run", "--project", Path.Combine(SourceRoot, "OpenNest.Console", "OpenNest.Console.csproj"), "--", run,
        };
        if (drawing != null) arguments.AddRange(["--drawing", drawing]);
        arguments.AddRange(["--plate", plateIndex.ToString()]);
        if (output != null) arguments.AddRange(["--output", output]);
        return arguments.ToArray();
    }

    public ProcessStartInfo StartInfo(string run, string mode)
    {
        var info = new ProcessStartInfo(Host)
        {
            WorkingDirectory = Directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "run", "--project", "unused", "--", run, "--drawing", mode })
            info.ArgumentList.Add(arg);
        return info;
    }

    public static async Task Ready(string run)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!File.Exists(run + ".ready")) await Task.Delay(25, timeout.Token);
    }

    public static Process GetProcess(string run, string name) => System.Diagnostics.Process.GetProcessById(
        int.Parse(File.ReadAllText(run + "." + name)));

    public static void ExitParent(string run) => File.WriteAllText(run + ".exit", "exit");

    public static void Release(string run) => File.WriteAllText(run + ".release", "release");

    public static void Stop(Process? process)
    {
        if (process == null || process.HasExited) return;
        process.Kill(entireProcessTree: true);
        Assert.True(process.WaitForExit(10_000), "Fixture child must be reaped.");
    }

    private const string ChildSource = """
        using System.Diagnostics;
        using System.Text.Json;

        if (args[0] == "descendant")
        {
            var path = args[1];
            File.WriteAllText(path + ".child", Environment.ProcessId.ToString());
            File.WriteAllText(path + ".ready", "ready");
            while (!File.Exists(path + ".release")) Thread.Sleep(25);
            return 0;
        }
        var separator = Array.IndexOf(args, "--");
        var input = args[separator + 1];
        var drawing = Array.IndexOf(args, "--drawing");
        var mode = drawing < 0 ? "normal" : args[drawing + 1];
        if (mode == "tree" || mode == "orphan")
        {
            File.WriteAllText(input + ".parent", Environment.ProcessId.ToString());
            var childInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            childInfo.ArgumentList.Add("descendant");
            childInfo.ArgumentList.Add(input);
            using var child = Process.Start(childInfo)!;
            if (mode == "orphan")
            {
                while (!File.Exists(input + ".exit")) Thread.Sleep(25);
                return 0;
            }
            while (!File.Exists(input + ".release")) Thread.Sleep(25);
            child.WaitForExit();
            return 0;
        }
        File.WriteAllText(input + ".parent", Environment.ProcessId.ToString());
        File.WriteAllText(input + ".ready", "ready");
        if (mode == "wait")
        {
            while (!File.Exists(input + ".release")) Thread.Sleep(25);
            return 0;
        }
        Console.WriteLine(JsonSerializer.Serialize(args));
        if (mode == "nonzero")
        {
            Console.Error.WriteLine("fixture stderr");
            return 7;
        }
        if (mode == "flood")
        {
            var stdout = Task.Run(() => Console.Write(new string('O', 256 * 1024)));
            var stderr = Task.Run(() => Console.Error.Write(new string('E', 256 * 1024)));
            Task.WaitAll(stdout, stderr);
        }
        return 0;
        """;
}
