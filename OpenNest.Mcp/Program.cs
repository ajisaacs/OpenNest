using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenNest.Engine.Jobs;
using OpenNest.Mcp;

NestingEngineRegistry.LoadPlugins(Path.Combine(AppContext.BaseDirectory, "Engines"));
var builder = Host.CreateApplicationBuilder(args);

// stdout carries the JSON-RPC stream; console logs must go to stderr or they corrupt it.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

using var toolGate = new SessionToolGate();
builder.Services.AddSingleton<NestSession>();
builder
    .Services.AddMcpServer()
    .WithRequestFilters(filters => filters.AddCallToolFilter(
        next => (context, token) => toolGate.RunAsync(ct => next(context, ct), token)))
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(Program).Assembly);

var app = builder.Build();
await app.RunAsync();
