# OpenNest Test Harness Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a console app + MCP tool that builds and runs OpenNest.Engine against a nest file, writing debug output to a file for grepping and saving the resulting nest.

**Architecture:** A new `OpenNest.TestHarness` console app references Core, Engine, and IO. It loads a nest file, clears a plate, runs `NestEngine.Fill()`, writes `Debug.WriteLine` output to a timestamped log file via `TextWriterTraceListener`, prints a summary to stdout, and saves the nest. An MCP tool `test_engine` in OpenNest.Mcp shells out to `dotnet run --project OpenNest.TestHarness` and returns the summary + log file path.

**Tech Stack:** .NET 8, System.Diagnostics tracing, OpenNest.Core/Engine/IO

---

## File Structure

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `OpenNest.TestHarness/OpenNest.TestHarness.csproj` | Console app project, references Core + Engine + IO. Forces `DEBUG` constant. |
| Create | `OpenNest.TestHarness/Program.cs` | Entry point: parse args, load nest, run fill, write debug to file, save nest |
| Modify | `OpenNest.sln` | Add new project to solution |
| Create | `OpenNest.Mcp/Tools/TestTools.cs` | MCP `test_engine` tool that shells out to the harness |

---

## Chunk 1: Console App + MCP Tool

### Task 1: Create the OpenNest.TestHarness project

**Files:**
- Create: `OpenNest.TestHarness/OpenNest.TestHarness.csproj`

- [ ] **Step 1: Create the project file**

Note: `DEBUG` is defined for all configurations so `Debug.WriteLine` output is always captured — that's the whole point of this tool.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <RootNamespace>OpenNest.TestHarness</RootNamespace>
    <AssemblyName>OpenNest.TestHarness</AssemblyName>
    <DefineConstants>$(DefineConstants);DEBUG;TRACE</DefineConstants>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\OpenNest.Core\OpenNest.Core.csproj" />
    <ProjectReference Include="..\OpenNest.Engine\OpenNest.Engine.csproj" />
    <ProjectReference Include="..\OpenNest.IO\OpenNest.IO.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add project to solution**

```bash
dotnet sln OpenNest.sln add OpenNest.TestHarness/OpenNest.TestHarness.csproj
```

- [ ] **Step 3: Verify it builds**

```bash
dotnet build OpenNest.TestHarness/OpenNest.TestHarness.csproj
```

Expected: Build succeeded (with warning about empty Program.cs — that's fine, we create it next).

---

### Task 2: Write the TestHarness Program.cs

**Files:**
- Create: `OpenNest.TestHarness/Program.cs`

The console app does:
1. Parse command-line args for nest file path, optional drawing name, plate index, output path
2. Create a timestamped log file and attach a `TextWriterTraceListener` so `Debug.WriteLine` goes to the file
3. Load the nest file via `NestReader`
4. Find the drawing and plate
5. Clear existing parts from the plate
6. Run `NestEngine.Fill()`
7. Print summary (part count, utilization, log file path) to stdout
8. Save the nest via `NestWriter`

- [ ] **Step 1: Write Program.cs**

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenNest;
using OpenNest.IO;

// Parse arguments.
var nestFile = args.Length > 0 ? args[0] : null;
var drawingName = (string)null;
var plateIndex = 0;
var outputFile = (string)null;

for (var i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--drawing" when i + 1 < args.Length:
            drawingName = args[++i];
            break;
        case "--plate" when i + 1 < args.Length:
            plateIndex = int.Parse(args[++i]);
            break;
        case "--output" when i + 1 < args.Length:
            outputFile = args[++i];
            break;
    }
}

if (string.IsNullOrEmpty(nestFile) || !File.Exists(nestFile))
{
    Console.Error.WriteLine("Usage: OpenNest.TestHarness <nest-file> [--drawing <name>] [--plate <index>] [--output <path>]");
    Console.Error.WriteLine("  nest-file  Path to a .zip nest file");
    Console.Error.WriteLine("  --drawing  Drawing name to fill with (default: first drawing)");
    Console.Error.WriteLine("  --plate    Plate index to fill (default: 0)");
    Console.Error.WriteLine("  --output   Output nest file path (default: <input>-result.zip)");
    return 1;
}

// Set up debug log file.
var logDir = Path.Combine(Path.GetDirectoryName(nestFile), "test-harness-logs");
Directory.CreateDirectory(logDir);
var logFile = Path.Combine(logDir, $"debug-{DateTime.Now:yyyyMMdd-HHmmss}.log");
var logWriter = new StreamWriter(logFile) { AutoFlush = true };
Trace.Listeners.Add(new TextWriterTraceListener(logWriter));

// Load nest.
var reader = new NestReader(nestFile);
var nest = reader.Read();

if (nest.Plates.Count == 0)
{
    Console.Error.WriteLine("Error: nest file contains no plates");
    return 1;
}

if (plateIndex >= nest.Plates.Count)
{
    Console.Error.WriteLine($"Error: plate index {plateIndex} out of range (0-{nest.Plates.Count - 1})");
    return 1;
}

var plate = nest.Plates[plateIndex];

// Find drawing.
var drawing = drawingName != null
    ? nest.Drawings.FirstOrDefault(d => d.Name == drawingName)
    : nest.Drawings.FirstOrDefault();

if (drawing == null)
{
    Console.Error.WriteLine(drawingName != null
        ? $"Error: drawing '{drawingName}' not found"
        : "Error: nest file contains no drawings");
    return 1;
}

// Clear existing parts.
var existingCount = plate.Parts.Count;
plate.Parts.Clear();

Console.WriteLine($"Nest: {nest.Name}");
Console.WriteLine($"Plate: {plateIndex} ({plate.Size.Width:F1} x {plate.Size.Height:F1}), spacing={plate.PartSpacing:F2}");
Console.WriteLine($"Drawing: {drawing.Name}");
Console.WriteLine($"Cleared {existingCount} existing parts");
Console.WriteLine("---");

// Run fill.
var sw = Stopwatch.StartNew();
var engine = new NestEngine(plate);
var item = new NestItem { Drawing = drawing, Quantity = 0 };
var success = engine.Fill(item);
sw.Stop();

// Flush and close the log.
Trace.Flush();
logWriter.Dispose();

// Print results.
Console.WriteLine($"Result: {(success ? "success" : "failed")}");
Console.WriteLine($"Parts placed: {plate.Parts.Count}");
Console.WriteLine($"Utilization: {plate.Utilization():P1}");
Console.WriteLine($"Time: {sw.ElapsedMilliseconds}ms");
Console.WriteLine($"Debug log: {logFile}");

// Save output.
if (outputFile == null)
{
    var dir = Path.GetDirectoryName(nestFile);
    var name = Path.GetFileNameWithoutExtension(nestFile);
    outputFile = Path.Combine(dir, $"{name}-result.zip");
}

var writer = new NestWriter(nest);
writer.Write(outputFile);
Console.WriteLine($"Saved: {outputFile}");

return 0;
```

- [ ] **Step 2: Build the project**

```bash
dotnet build OpenNest.TestHarness/OpenNest.TestHarness.csproj
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 3: Run a smoke test with the real nest file**

```bash
dotnet run --project OpenNest.TestHarness -- "C:\Users\AJ\Desktop\4980 A24 PT02 60x120  45pcs v2.zip"
```

Expected: Prints nest info and results to stdout, writes debug log file, saves a `-result.zip` file.

- [ ] **Step 4: Commit**

```bash
git add OpenNest.TestHarness/ OpenNest.sln
git commit -m "feat: add OpenNest.TestHarness console app for engine testing"
```

---

### Task 3: Add the MCP test_engine tool

**Files:**
- Create: `OpenNest.Mcp/Tools/TestTools.cs`

The MCP tool:
1. Accepts optional `nestFile`, `drawingName`, `plateIndex` parameters
2. Runs `dotnet run --project <path> -- <args>` capturing stdout (results) and stderr (errors only)
3. Returns the summary + debug log file path (Claude can then Grep the log file)

Note: The solution root is hard-coded because the MCP server is published to `~/.claude/mcp/OpenNest.Mcp/`, far from the source tree.

- [ ] **Step 1: Create TestTools.cs**

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using ModelContextProtocol.Server;

namespace OpenNest.Mcp.Tools
{
    [McpServerToolType]
    public class TestTools
    {
        private const string SolutionRoot = @"C:\Users\AJ\Desktop\Projects\OpenNest";

        private static readonly string HarnessProject = Path.Combine(
            SolutionRoot, "OpenNest.TestHarness", "OpenNest.TestHarness.csproj");

        [McpServerTool(Name = "test_engine")]
        [Description("Build and run the nesting engine against a nest file. Returns fill results and a debug log file path for grepping. Use this to test engine changes without restarting the MCP server.")]
        public string TestEngine(
            [Description("Path to the nest .zip file")] string nestFile = @"C:\Users\AJ\Desktop\4980 A24 PT02 60x120  45pcs v2.zip",
            [Description("Drawing name to fill with (default: first drawing)")] string drawingName = null,
            [Description("Plate index to fill (default: 0)")] int plateIndex = 0,
            [Description("Output nest file path (default: <input>-result.zip)")] string outputFile = null)
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
```

- [ ] **Step 2: Build the MCP project**

```bash
dotnet build OpenNest.Mcp/OpenNest.Mcp.csproj
```

Expected: Build succeeded.

- [ ] **Step 3: Republish the MCP server**

```bash
dotnet publish OpenNest.Mcp/OpenNest.Mcp.csproj -c Release -o "$USERPROFILE/.claude/mcp/OpenNest.Mcp"
```

Expected: Publish succeeded. The MCP server now has the `test_engine` tool.

- [ ] **Step 4: Commit**

```bash
git add OpenNest.Mcp/Tools/TestTools.cs
git commit -m "feat: add test_engine MCP tool for iterative engine testing"
```

---

## Usage

After implementation, the workflow for iterating on FillLinear becomes:

1. **Other session** makes changes to `FillLinear.cs` or `NestEngine.cs`
2. **This session** calls `test_engine` (no args needed — defaults to the test nest file)
3. The tool builds the latest code and runs it in a fresh process
4. Returns: part count, utilization, timing, and **debug log file path**
5. Grep the log file for specific patterns (e.g., `[FillLinear]`, `[FindBestFill]`)
6. Repeat
