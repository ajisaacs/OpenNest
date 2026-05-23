using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  StreamGravographJob <file.prn>   <COMx> [chunk=256] [flow=rtscts|xonxoff|none]");
    Console.Error.WriteLine("  StreamGravographJob --gen <name> <outfile.prn>      # name: testA | testB | miniB | miniSquare");
    Console.Error.WriteLine("  StreamGravographJob --inspect-nest <file.nest>");
    Console.Error.WriteLine("  StreamGravographJob --from-nest    <file.nest> <outfile.prn>");
    return 2;
}

// Inspect a .nest file: extract polylines via the post-processor pipeline and
// report dimensions / bounding box / pen-up travel — no bytes written.
if (args[0] == "--inspect-nest")
{
    if (args.Length < 2) { Console.Error.WriteLine("--inspect-nest requires <file.nest>"); return 2; }
    var nestPath = args[1];
    if (!File.Exists(nestPath)) { Console.Error.WriteLine($"Not found: {nestPath}"); return 3; }

    using var fs = new FileStream(nestPath, FileMode.Open, FileAccess.Read);
    var reader = new OpenNest.IO.NestReader(fs);
    var nest = reader.Read();

    Console.WriteLine($"Nest: {nest.Name}");
    Console.WriteLine($"Units: {nest.Units}");
    Console.WriteLine($"Plates: {nest.Plates.Count}");
    var plateIdx = 0;
    foreach (var plate in nest.Plates)
    {
        plateIdx++;
        Console.WriteLine($"  Plate {plateIdx}: size={plate.Size.Length} x {plate.Size.Width}, quadrant={plate.Quadrant}, parts={plate.Parts.Count}");
    }

    var polylines = new OpenNest.Posts.GravographIS.NestPolylineExtractor().Extract(nest);
    if (polylines.Count == 0)
    {
        Console.WriteLine("No polylines extracted.");
        return 0;
    }

    double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
    double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
    int totalPts = 0;
    foreach (var p in polylines)
    {
        foreach (var v in p)
        {
            if (v.X < minX) minX = v.X;
            if (v.X > maxX) maxX = v.X;
            if (v.Y < minY) minY = v.Y;
            if (v.Y > maxY) maxY = v.Y;
        }
        totalPts += p.Count;
    }
    Console.WriteLine($"Polylines: {polylines.Count}, total points: {totalPts}");
    Console.WriteLine($"Bounding box (inches): X ∈ [{minX:F3}, {maxX:F3}]  Y ∈ [{minY:F3}, {maxY:F3}]");
    Console.WriteLine($"Extents: {maxX - minX:F3}\" × {maxY - minY:F3}\"");

    // After running the pre-pass (stitch + reorder from origin) — what the writer will actually consume.
    var prepared = OpenNest.Posts.GravographIS.PolylinePrePass.Prepare(polylines);
    Console.WriteLine($"After stitch+reorder: {prepared.Count} polylines");

    Console.WriteLine();
    Console.WriteLine("--- Vertex dump (prepared, upper-left origin, with segment deltas) ---");
    var pi = 0;
    foreach (var poly in prepared)
    {
        pi++;
        Console.WriteLine($"Polyline {pi}: {poly.Count} points");
        var cumX = 0.0; var cumY = 0.0;
        for (var i = 0; i < poly.Count; i++)
        {
            var v = poly[i];
            if (i == 0)
            {
                Console.WriteLine($"  [{i}] ({v.X,7:F3}, {v.Y,7:F3})   first DR travel from upper-left origin=({v.X,+7:F3}, {v.Y,+7:F3})");
            }
            else
            {
                var dx = v.X - poly[i - 1].X;
                var dy = v.Y - poly[i - 1].Y;
                cumX += dx;
                cumY += dy;
                Console.WriteLine($"  [{i}] ({v.X,7:F3}, {v.Y,7:F3})   Δ=({dx,+7:F3}, {dy,+7:F3})   cum from origin=({cumX,+7:F3}, {cumY,+7:F3})");
            }
        }
    }
    return 0;
}

// Convert a .nest file to a .prn job via the full post-processor pipeline.
if (args[0] == "--from-nest")
{
    if (args.Length < 3) { Console.Error.WriteLine("--from-nest requires <file.nest> <outfile.prn>"); return 2; }
    var nestPath = args[1];
    var outFile = args[2];
    if (!File.Exists(nestPath)) { Console.Error.WriteLine($"Not found: {nestPath}"); return 3; }

    using var fs = new FileStream(nestPath, FileMode.Open, FileAccess.Read);
    var nest = new OpenNest.IO.NestReader(fs).Read();
    var post = new OpenNest.Posts.GravographIS.GravographISPostProcessor();
    post.Post(nest, outFile);

    var size = new FileInfo(outFile).Length;
    Console.WriteLine($"Wrote {size} bytes → {outFile}");
    return 0;
}

// Generator mode: run the live writer to produce a captured-test file on disk.
if (args[0] == "--gen")
{
    if (args.Length < 3) { Console.Error.WriteLine("--gen requires <name> <outfile>"); return 2; }
    var preset = args[1];
    var outFile = args[2];
    var polylines = preset.ToLowerInvariant() switch
    {
        "testa" => new System.Collections.Generic.List<System.Collections.Generic.IReadOnlyList<OpenNest.Geometry.Vector>>
        {
            new[] { new OpenNest.Geometry.Vector(1, 1), new OpenNest.Geometry.Vector(1, 3) },
        },
        "testb" => new System.Collections.Generic.List<System.Collections.Generic.IReadOnlyList<OpenNest.Geometry.Vector>>
        {
            new[] { new OpenNest.Geometry.Vector(1, 1), new OpenNest.Geometry.Vector(1, 3) },
            new[] { new OpenNest.Geometry.Vector(4, 1), new OpenNest.Geometry.Vector(4, 3) },
            new[] { new OpenNest.Geometry.Vector(4, 5), new OpenNest.Geometry.Vector(4, 7) },
            new[] { new OpenNest.Geometry.Vector(1, 5), new OpenNest.Geometry.Vector(1, 7) },
        },
        // Same 4-polyline topology as testB (vertical lines + diagonal PU travels between them),
        // shrunk to a 0.5" × 1.5" footprint so it stays right near the operator-set work origin.
        "minib" => new System.Collections.Generic.List<System.Collections.Generic.IReadOnlyList<OpenNest.Geometry.Vector>>
        {
            new[] { new OpenNest.Geometry.Vector(0,   0),   new OpenNest.Geometry.Vector(0,   0.5) },
            new[] { new OpenNest.Geometry.Vector(0.5, 0),   new OpenNest.Geometry.Vector(0.5, 0.5) },
            new[] { new OpenNest.Geometry.Vector(0.5, 1),   new OpenNest.Geometry.Vector(0.5, 1.5) },
            new[] { new OpenNest.Geometry.Vector(0,   1),   new OpenNest.Geometry.Vector(0,   1.5) },
        },
        // Closed 0.5" square as a SINGLE polyline of 5 points → 4-segment PD packet.
        // Exercises multi-segment PD (one FF FD 50 44 00 00 followed by 4 records,
        // no intermediate lifts) and bi-directional motion (X+, Y+, X−, Y−).
        // Returns the head to its starting point so no manual jog needed after.
        "minisquare" => new System.Collections.Generic.List<System.Collections.Generic.IReadOnlyList<OpenNest.Geometry.Vector>>
        {
            new[]
            {
                new OpenNest.Geometry.Vector(0,    0),
                new OpenNest.Geometry.Vector(0.5,  0),
                new OpenNest.Geometry.Vector(0.5,  0.5),
                new OpenNest.Geometry.Vector(0,    0.5),
                new OpenNest.Geometry.Vector(0,    0),
            },
        },
        _ => throw new ArgumentException($"Unknown preset '{preset}' (try testA, testB, miniB, or miniSquare)."),
    };

    using var outFs = new FileStream(outFile, FileMode.Create, FileAccess.Write);
    new OpenNest.Posts.GravographIS.GravographISWriter().Write(polylines, outFs);
    Console.WriteLine($"Wrote {new FileInfo(outFile).Length} bytes via live writer → {outFile}");
    return 0;
}

var file = args[0];
var portName = args[1];
var chunk = args.Length > 2 ? int.Parse(args[2]) : 256;
var flowArg = args.Length > 3 ? args[3] : "rtscts";

var handshake = flowArg.ToLowerInvariant() switch
{
    "rtscts" or "rts" or "cts" => Handshake.RequestToSend,
    "xonxoff" or "xon" or "xoff" => Handshake.XOnXOff,
    "none" => Handshake.None,
    _ => throw new ArgumentException($"Unknown flow control '{flowArg}'."),
};

if (!File.Exists(file))
{
    Console.Error.WriteLine($"File not found: {file}");
    return 3;
}

var bytes = File.ReadAllBytes(file);
Console.WriteLine($"File: {file}");
Console.WriteLine($"Size: {bytes.Length} bytes");
Console.WriteLine($"Header: {BitConverter.ToString(bytes, 0, Math.Min(7, bytes.Length)).Replace('-', ' ')}");

var ports = SerialPort.GetPortNames();
Array.Sort(ports);
Console.WriteLine($"Available COM ports: {string.Join(", ", ports)}");
if (Array.IndexOf(ports, portName) < 0)
{
    Console.Error.WriteLine($"{portName} not in available ports.");
    return 4;
}

using var port = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One)
{
    Handshake = handshake,
    WriteTimeout = 30000,
    ReadTimeout = 30000,
    WriteBufferSize = 4096,
    DtrEnable = true,
};

// Probe with the same CreateFile flags SerialStream uses, in this same process,
// so we can tell SerialStream-specific failures apart from process-level access denials.
{
    const uint GENERIC_RW = 0x80000000u | 0x40000000u;
    const uint OPEN_EXISTING = 3;
    const uint FILE_FLAG_OVERLAPPED = 0x40000000u;
    var devName = @"\\.\" + portName;
    var handle = NativeMethods.CreateFileW(devName, GENERIC_RW, 0, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
    var err = Marshal.GetLastWin32Error();
    if (handle.IsInvalid)
    {
        Console.WriteLine($"CreateFile(\"{devName}\", overlapped, exclusive) FAILED: win32={err} ({new Win32Exception(err).Message})");
    }
    else
    {
        Console.WriteLine($"CreateFile(\"{devName}\", overlapped, exclusive) OK — closing.");
        handle.Close();
    }
}

Console.WriteLine($"Opening {portName} 9600 8N1 handshake={handshake}...");
port.Open();
Console.WriteLine("Opened.");

var sw = Stopwatch.StartNew();
try
{
    for (var i = 0; i < bytes.Length; i += chunk)
    {
        var n = Math.Min(chunk, bytes.Length - i);
        port.Write(bytes, i, n);
    }
    try { port.BaseStream.Flush(); } catch { /* advisory */ }
    Thread.Sleep(500);
}
finally
{
    sw.Stop();
    port.Close();
}

Console.WriteLine($"Sent {bytes.Length} bytes in {sw.ElapsedMilliseconds} ms. Port closed.");
return 0;

internal static class NativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    internal static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);
}
