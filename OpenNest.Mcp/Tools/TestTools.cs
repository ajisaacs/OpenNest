using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

namespace OpenNest.Mcp.Tools
{
    [McpServerToolType]
    public class TestTools
    {
        private readonly IConfiguration configuration;
        private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);

        public TestTools(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        [McpServerTool(Name = "test_engine")]
        [Description(
            "Build and run the nesting engine against a nest file. Returns fill results and a debug log file path for grepping. Use this to test engine changes without restarting the MCP server."
        )]
        public async Task<string> TestEngine(
            [Description("Path to the nest .nest file")] string nestFile,
            [Description("Drawing name to fill with (default: first drawing)")] string drawingName = null,
            [Description("Plate index to fill (default: 0)")] int plateIndex = 0,
            [Description("Output nest file path (default: <input>-result.nest)")] string outputFile = null,
            CancellationToken cancellationToken = default
        )
        {
            if (!File.Exists(nestFile))
                return $"Error: nest file not found: {nestFile}";

            using var process = new Process();
            var started = false;
            Task completion = null;
            try
            {
                var sourceRoot = configuration["EngineHarness:SourceRoot"];
                if (string.IsNullOrWhiteSpace(sourceRoot) || !Path.IsPathFullyQualified(sourceRoot))
                    return "Error: EngineHarness:SourceRoot must be an absolute OpenNest checkout path.";

                var harnessProject = Path.Combine(sourceRoot, "OpenNest.Console", "OpenNest.Console.csproj");
                if (!File.Exists(harnessProject))
                    return $"Error: harness project not found: {harnessProject}";

                var dotnetPath = configuration["EngineHarness:DotnetPath"] ?? Path.GetFullPath(Path.Combine(
                    RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..",
                    OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
                if (!Path.IsPathFullyQualified(dotnetPath) || !File.Exists(dotnetPath))
                    return "Error: EngineHarness:DotnetPath must identify an existing absolute dotnet executable.";

                if (!int.TryParse(configuration["EngineHarness:TimeoutSeconds"] ?? "120",
                    NumberStyles.None, CultureInfo.InvariantCulture, out var timeoutSeconds)
                    || timeoutSeconds <= 0 || timeoutSeconds > int.MaxValue / 1000)
                    return "Error: EngineHarness:TimeoutSeconds must be a positive finite deadline (at most 2147483 seconds).";

                var psi = new ProcessStartInfo(dotnetPath)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = sourceRoot,
                };
                foreach (var argument in new[] { "run", "--project", harnessProject, "--", Path.GetFullPath(nestFile) })
                    psi.ArgumentList.Add(argument);
                if (!string.IsNullOrEmpty(drawingName))
                {
                    psi.ArgumentList.Add("--drawing");
                    psi.ArgumentList.Add(drawingName);
                }
                psi.ArgumentList.Add("--plate");
                psi.ArgumentList.Add(plateIndex.ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrEmpty(outputFile))
                {
                    psi.ArgumentList.Add("--output");
                    psi.ArgumentList.Add(Path.GetFullPath(outputFile));
                }
                process.StartInfo = psi;

                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                deadline.Token.ThrowIfCancellationRequested();
                started = process.Start();
                if (!started)
                    return "Error running test harness: process did not start.";

                var stdoutTask = process.StandardOutput.ReadToEndAsync(deadline.Token);
                var stderrTask = process.StandardError.ReadToEndAsync(deadline.Token);
                completion = Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdoutTask, stderrTask);
                // Pipe EOF can outlive the direct process, so bound the aggregate as well.
                await completion.WaitAsync(deadline.Token);
                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                var result = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(stdout))
                    result.Append(stdout.TrimEnd());
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    result.AppendLine();
                    result.AppendLine();
                    result.AppendLine("=== Errors ===");
                    result.Append(stderr.TrimEnd());
                }
                if (process.ExitCode != 0)
                {
                    result.AppendLine();
                    result.AppendLine($"Process exited with code {process.ExitCode}");
                }
                return result.ToString();
            }
            catch (Exception ex)
            {
                var error = $"Error running test harness: {ex.Message}";
                if (ex is OperationCanceledException)
                {
                    var reason = cancellationToken.IsCancellationRequested ? "cancelled" : "timed out";
                    error = $"Error running test harness: {reason}.";
                }
                if (started)
                    error += await StopProcessAsync(process);
                return error;
            }
            finally
            {
                if (started)
                {
                    // Close our pipe ends even if a detached descendant still owns writers.
                    process.StandardOutput.Dispose();
                    process.StandardError.Dispose();
                }
                if (completion != null)
                    _ = completion.ContinueWith(task => _ = task.Exception, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
            }
        }

        private static async Task<string> StopProcessAsync(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(CleanupTimeout);
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $" Cleanup failed: {ex.Message}";
            }
        }
    }
}
