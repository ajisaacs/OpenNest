using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace OpenNest.Server.Tests;

/// <summary>
/// Hosts the production routes in memory against a private, disposable SQLite file.
/// The production <see cref="NestDatabase"/> registration is replaced before anything
/// resolves it, so tests never open the default data/nests.db or an OPENNEST_DB path.
/// The update mirror never refreshes on its own and reaches GitHub only through
/// <c>updateHandler</c> (by default a handler that fails every request); tests refresh it explicitly.
/// Disposing the factory disposes the host (and its database) before deleting the directory.
/// </summary>
public sealed class ServerFactory : WebApplicationFactory<global::Program>
{
    private bool _cleanedUp;
    private NestDatabase? _database;
    private readonly HttpMessageHandler? _updateHandler;

    public ServerFactory(string? databasePath = null, HttpMessageHandler? updateHandler = null)
    {
        _updateHandler = updateHandler;
        DirectoryPath = Path.Combine(Path.GetTempPath(), "opennest-server-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        DatabasePath = databasePath ?? Path.Combine(DirectoryPath, "nests.db");
    }

    /// <summary>Private temporary directory owned by this factory.</summary>
    public string DirectoryPath { get; }

    public string DatabasePath { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<NestDatabase>();
            // Register an externally owned instance; this factory closes it before deleting its file.
            services.AddSingleton(_database = new NestDatabase(DatabasePath, pooling: false));

            services.RemoveAll<UpdateMirror>();
            var hosted = services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(UpdateMirrorService)).ToList();
            foreach (var descriptor in hosted)
                services.Remove(descriptor);
            services.AddSingleton(_ => new UpdateMirror(
                Path.Combine(DirectoryPath, "updates"), new HttpClient(_updateHandler ?? new OfflineHandler())));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing || _cleanedUp)
            return;

        _cleanedUp = true;
        _database?.Dispose();
        if (Directory.Exists(DirectoryPath))
            Directory.Delete(DirectoryPath, recursive: true);
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Tests never reach GitHub.");
    }
}
