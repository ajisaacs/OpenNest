using OpenNest.Data;

namespace OpenNest.Server;

public sealed partial class NestDatabase
{
    private const string NestSequenceSchema = """
        CREATE TABLE IF NOT EXISTS nest_sequences (
            year INTEGER PRIMARY KEY,
            next INTEGER NOT NULL
        );
        PRAGMA user_version=6;
        """;

    /// <summary>
    /// Allocates the next nest number for <paramref name="year"/>, shared by every client.
    /// The number is also kept above every stored generated name for the year (active or
    /// deleted), so names created offline or before the server counted are never reissued.
    /// </summary>
    public int AllocateNestNumber(int year)
    {
        if (year is < NestNames.MinYear or > NestNames.MaxYear)
            throw new ArgumentOutOfRangeException(nameof(year));

        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            var next = 1;
            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT next FROM nest_sequences WHERE year=$year";
                command.Parameters.AddWithValue("$year", year);
                if (command.ExecuteScalar() is long stored)
                    next = checked((int)stored);
            }

            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT name FROM nests WHERE name LIKE $prefix";
                command.Parameters.AddWithValue("$prefix", $"N{year % 100:D2}-%");
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (NestNames.TryParse(reader.GetString(0), year, out var used) && used >= next)
                        next = checked(used + 1);
                }
            }

            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO nest_sequences (year, next) VALUES ($year, $next)
                    ON CONFLICT(year) DO UPDATE SET next = excluded.next
                    """;
                command.Parameters.AddWithValue("$year", year);
                command.Parameters.AddWithValue("$next", checked(next + 1));
                command.ExecuteNonQuery();
            }

            transaction.Commit();
            return next;
        }
    }
}
