using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace OpenNest.Server.Tests;

/// <summary>
/// Hosts the production routes in memory against a private, disposable SQLite file.
/// The production <see cref="NestDatabase"/> registration is replaced before anything
/// resolves it, so tests never open the default data/nests.db or an OPENNEST_DB path.
/// Disposing the factory disposes the host (and its database) before deleting the directory.
/// </summary>
public sealed class ServerFactory : WebApplicationFactory<global::Program>
{
    private bool _cleanedUp;

    public ServerFactory(string? databasePath = null)
    {
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
            services.AddSingleton(_ => new NestDatabase(DatabasePath, pooling: false));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing || _cleanedUp)
            return;

        _cleanedUp = true;
        if (Directory.Exists(DirectoryPath))
            Directory.Delete(DirectoryPath, recursive: true);
    }
}
