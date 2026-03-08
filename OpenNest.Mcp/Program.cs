using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using OpenNest.Mcp;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<NestSession>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(Program).Assembly);

var app = builder.Build();
await app.RunAsync();
