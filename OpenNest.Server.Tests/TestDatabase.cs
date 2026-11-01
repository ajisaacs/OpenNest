using Microsoft.Data.Sqlite;

namespace OpenNest.Server.Tests;

/// <summary>
/// Raw SQL against a test server's SQLite file, for checks the API does not expose. Import with
/// <c>using static</c>.
/// </summary>
internal static class TestDatabase
{
    public static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public static object? Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
