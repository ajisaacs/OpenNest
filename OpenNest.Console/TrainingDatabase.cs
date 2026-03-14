using System;
using Microsoft.Data.Sqlite;
using OpenNest.Engine.ML;

namespace OpenNest.Console
{
    public class TrainingDatabase : IDisposable
    {
        private readonly SqliteConnection _connection;

        public TrainingDatabase(string dbPath)
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();

            _connection = new SqliteConnection(connectionString);
            _connection.Open();

            InitializeSchema();
        }

        private void InitializeSchema()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Parts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FileName TEXT,
                    Area REAL,
                    Convexity REAL,
                    AspectRatio REAL,
                    BBFill REAL,
                    Circularity REAL,
                    VertexCount INTEGER,
                    Bitmask BLOB,
                    GeometryData TEXT
                );

                CREATE TABLE IF NOT EXISTS Runs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PartId INTEGER,
                    SheetWidth REAL,
                    SheetHeight REAL,
                    Spacing REAL,
                    PartCount INTEGER,
                    Utilization REAL,
                    TimeMs INTEGER,
                    LayoutData TEXT,
                    FilePath TEXT,
                    FOREIGN KEY(PartId) REFERENCES Parts(Id)
                );

                CREATE INDEX IF NOT EXISTS idx_parts_filename ON Parts(FileName);
                CREATE INDEX IF NOT EXISTS idx_runs_partid ON Runs(PartId);
            ";
            cmd.ExecuteNonQuery();
        }

        public long GetOrAddPart(string fileName, PartFeatures features, string geometryData)
        {
            // Check if part already exists
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT Id FROM Parts WHERE FileName = @name";
                checkCmd.Parameters.AddWithValue("@name", fileName);
                var result = checkCmd.ExecuteScalar();
                if (result != null) return (long)result;
            }

            // Add new part
            using (var insertCmd = _connection.CreateCommand())
            {
                insertCmd.CommandText = @"
                    INSERT INTO Parts (FileName, Area, Convexity, AspectRatio, BBFill, Circularity, VertexCount, Bitmask, GeometryData)
                    VALUES (@name, @area, @conv, @asp, @fill, @circ, @vert, @mask, @geo);
                    SELECT last_insert_rowid();";

                insertCmd.Parameters.AddWithValue("@name", fileName);
                insertCmd.Parameters.AddWithValue("@area", features.Area);
                insertCmd.Parameters.AddWithValue("@conv", features.Convexity);
                insertCmd.Parameters.AddWithValue("@asp", features.AspectRatio);
                insertCmd.Parameters.AddWithValue("@fill", features.BoundingBoxFill);
                insertCmd.Parameters.AddWithValue("@circ", features.Circularity);
                insertCmd.Parameters.AddWithValue("@vert", features.VertexCount);
                insertCmd.Parameters.AddWithValue("@mask", features.Bitmask);
                insertCmd.Parameters.AddWithValue("@geo", geometryData);

                return (long)insertCmd.ExecuteScalar();
            }
        }

        public bool HasRun(string fileName, double sheetWidth, double sheetHeight, double spacing)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"SELECT COUNT(*) FROM Runs r JOIN Parts p ON r.PartId = p.Id
                WHERE p.FileName = @name AND r.SheetWidth = @w AND r.SheetHeight = @h AND r.Spacing = @s";
            cmd.Parameters.AddWithValue("@name", fileName);
            cmd.Parameters.AddWithValue("@w", sheetWidth);
            cmd.Parameters.AddWithValue("@h", sheetHeight);
            cmd.Parameters.AddWithValue("@s", spacing);
            return (long)cmd.ExecuteScalar() > 0;
        }

        public int RunCount(string fileName)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Runs r JOIN Parts p ON r.PartId = p.Id WHERE p.FileName = @name";
            cmd.Parameters.AddWithValue("@name", fileName);
            return (int)(long)cmd.ExecuteScalar();
        }

        public void AddRun(long partId, double w, double h, double s, BruteForceResult result, string filePath)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Runs (PartId, SheetWidth, SheetHeight, Spacing, PartCount, Utilization, TimeMs, LayoutData, FilePath)
                VALUES (@pid, @w, @h, @s, @cnt, @util, @time, @layout, @path)";

            cmd.Parameters.AddWithValue("@pid", partId);
            cmd.Parameters.AddWithValue("@w", w);
            cmd.Parameters.AddWithValue("@h", h);
            cmd.Parameters.AddWithValue("@s", s);
            cmd.Parameters.AddWithValue("@cnt", result.PartCount);
            cmd.Parameters.AddWithValue("@util", result.Utilization);
            cmd.Parameters.AddWithValue("@time", result.TimeMs);
            cmd.Parameters.AddWithValue("@layout", result.LayoutData ?? "");
            cmd.Parameters.AddWithValue("@path", filePath ?? "");

            cmd.ExecuteNonQuery();
        }

        public SqliteTransaction BeginTransaction()
        {
            return _connection.BeginTransaction();
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }
    }
}
