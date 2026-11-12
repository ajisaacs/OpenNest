using System.Diagnostics;
using OpenNest.Data;
using OpenNest.Server.Worker;

// The nest server's worker: takes queued whole-job nests from OPENNEST_SERVER_URL and solves
// them one at a time, and measures library drawings for the library's size filters. Run it in
// its own container with CPU and memory limits (compose.server.yaml) so nesting never slows
// nest open and save. See docs/server-nesting.md.
var options = WorkerOptions.FromEnvironment(Environment.GetEnvironmentVariable);

// Below the server and anything else on the host when they share CPUs; the container's
// CPU limit is the real bound.
try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; }
catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or PlatformNotSupportedException) { }

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(_ => new RemoteNestJobs(options.ServerUrl));
builder.Services.AddSingleton(sp => new NestJobWorker(
    sp.GetRequiredService<RemoteNestJobs>(), sp.GetRequiredService<ILogger<NestJobWorker>>()));
builder.Services.AddHostedService<NestJobWorkerService>();
// Measures library drawings for the size and hole filters; see DrawingGeometryBackfill.
builder.Services.AddSingleton(_ => new RemoteDrawingGeometry(options.ServerUrl));
builder.Services.AddSingleton<DrawingGeometryBackfill>();
builder.Services.AddHostedService<DrawingGeometryBackfillService>();
// A running job is given back, not abandoned, when the container stops.
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(20));
builder.Build().Run();
