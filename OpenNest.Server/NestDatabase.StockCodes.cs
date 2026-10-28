using Microsoft.Data.Sqlite;
using OpenNest.Data;

namespace OpenNest.Server;

// The ledger item codes each nest's current archive is coded with, kept beside the record at
// every archive write so listing the codes in use never reopens an archive.
public sealed partial class NestDatabase
{
    // v14: one row per distinct code of a nest's current archive.
    private const string StockCodeSchema = """
        CREATE TABLE IF NOT EXISTS nest_stock_codes (
            id   TEXT NOT NULL,
            code TEXT NOT NULL,
            PRIMARY KEY (id, code)
        );
        CREATE INDEX IF NOT EXISTS ix_nest_stock_codes_code ON nest_stock_codes(code);
        """;

    /// <summary>
    /// The item codes on the current archives of every non-deleted Draft, Quote or To Be Cut
    /// nest except <paramref name="excludeId"/>, ordered by code then nest name.
    /// </summary>
    public IReadOnlyList<NestStockCodeUse> StockCodesInUse(Guid? excludeId)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT c.code, n.id, n.name, n.status FROM nest_stock_codes c
                JOIN nests n ON n.id = c.id
                WHERE n.deleted = 0 AND n.status IN ('Draft', 'Quote', 'ToBeCut') AND n.id <> $exclude
                ORDER BY c.code, n.name COLLATE NOCASE, n.id
                """;
            command.Parameters.AddWithValue("$exclude", (excludeId ?? Guid.Empty).ToString());
            var uses = new List<NestStockCodeUse>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                uses.Add(new NestStockCodeUse
                {
                    ItemCode = reader.GetString(0),
                    NestId = Guid.Parse(reader.GetString(1)),
                    NestName = reader.GetString(2),
                    Status = Enum.TryParse<NestStatus>(reader.GetString(3), true, out var status) ? status : NestStatus.Quote,
                });
            }
            return uses;
        }
    }

    // Called only under _sync with the caller's transaction, whenever a nest's current archive changes.
    private void WriteStockCodes(Guid id, byte[] file, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM nest_stock_codes WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.ExecuteNonQuery();
        InsertStockCodes(command, id, NestArchive.ReadStockItemCodes(file));
    }

    private static void InsertStockCodes(SqliteCommand command, Guid id, IEnumerable<string> codes)
    {
        command.CommandText = "INSERT OR IGNORE INTO nest_stock_codes (id, code) VALUES ($id, $code)";
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$id", id.ToString());
        var code = command.Parameters.Add("$code", SqliteType.Text);
        foreach (var value in codes)
        {
            code.Value = value;
            command.ExecuteNonQuery();
        }
        command.Parameters.Clear();
    }

    // v14: reads each current archive once, deleted ones too so a recovered nest keeps its codes.
    private static void AddStockCodes(SqliteCommand command)
    {
        command.CommandText = StockCodeSchema;
        command.ExecuteNonQuery();
        var codes = new List<(Guid Id, IReadOnlyList<string> Codes)>();
        command.CommandText = "SELECT id, file FROM nests";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                codes.Add((Guid.Parse(reader.GetString(0)), NestArchive.ReadStockItemCodes((byte[])reader.GetValue(1))));
        }
        foreach (var (id, nestCodes) in codes.Where(c => c.Codes.Count > 0))
            InsertStockCodes(command, id, nestCodes);
        command.CommandText = "PRAGMA user_version=14";
        command.ExecuteNonQuery();
    }
}
