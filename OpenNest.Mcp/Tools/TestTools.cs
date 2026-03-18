using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace OpenNest.Mcp.Tools
{
    [McpServerToolType]
    public class TestTools
    {
        private const string SolutionRoot = @"C:\Users\AJ\Desktop\Projects\OpenNest";

        private static readonly string HarnessProject = Path.Combine(
            SolutionRoot, "OpenNest.Console", "OpenNest.Console.csproj");

        [McpServerTool(Name = "test_engine")]
        [Description("Build and run the nesting engine against a nest file. Returns fill results and a debug log file path for grepping. Use this to test engine changes without restarting the MCP server.")]
        public string TestEngine(
            [Description("Path to the nest .opnest file")] string nestFile = @"C:\Users\AJ\Desktop\4980 A24 PT02 60x120  45pcs v2.opnest",
            [Description("Drawing name to fill with (default: first drawing)")] string drawingName = null,
            [Description("Plate index to fill (default: 0)")] int plateIndex = 0,
            [Description("Output nest file path (default: <input>-result.opnest)")] string outputFile = null)
        {
            if (!File.Exists(nestFile))
                return $"Error: nest file not found: {nestFile}";

            var processArgs = new StringBuilder();
            processArgs.Append($"\"{nestFile}\"");

            if (!string.IsNullOrEmpty(drawingName))
                processArgs.Append($" --drawing \"{drawingName}\"");

            processArgs.Append($" --plate {plateIndex}");

            if (!string.IsNullOrEmpty(outputFile))
                processArgs.Append($" --output \"{outputFile}\"");

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{HarnessProject}\" -- {processArgs}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = SolutionRoot
            };

            var sb = new StringBuilder();

            try
            {
                using var process = Process.Start(psi);
                var stderrTask = process.StandardError.ReadToEndAsync();
                var stdout = process.StandardOutput.ReadToEnd();
                process.WaitForExit(120_000);
                var stderr = stderrTask.Result;

                if (!string.IsNullOrWhiteSpace(stdout))
                    sb.Append(stdout.TrimEnd());

                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    sb.AppendLine();
                    sb.AppendLine();
                    sb.AppendLine("=== Errors ===");
                    sb.Append(stderr.TrimEnd());
                }

                if (process.ExitCode != 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"Process exited with code {process.ExitCode}");
                }
            }
            catch (System.Exception ex)
            {
                sb.AppendLine($"Error running test harness: {ex.Message}");
            }

            return sb.ToString();
        }
    }
}
