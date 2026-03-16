using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OpenNest.Engine.ML;
using OpenNest.IO;
using OpenNest.Training.Data;

namespace OpenNest.Training
{
    public class TrainingDatabase : IDisposable
    {
        private readonly TrainingDbContext _db;

        public TrainingDatabase(string dbPath)
        {
            if (!dbPath.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                dbPath += ".db";

            _db = new TrainingDbContext(dbPath);
            _db.Database.EnsureCreated();
            MigrateSchema();
        }

        public long GetOrAddPart(string fileName, PartFeatures features, string geometryData)
        {
            var existing = _db.Parts.FirstOrDefault(p => p.FileName == fileName);
            if (existing != null) return existing.Id;

            var part = new TrainingPart
            {
                FileName = fileName,
                Area = features.Area,
                Convexity = features.Convexity,
                AspectRatio = features.AspectRatio,
                BBFill = features.BoundingBoxFill,
                Circularity = features.Circularity,
                PerimeterToAreaRatio = features.PerimeterToAreaRatio,
                VertexCount = features.VertexCount,
                Bitmask = features.Bitmask,
                GeometryData = geometryData
            };

            _db.Parts.Add(part);
            _db.SaveChanges();
            return part.Id;
        }

        public bool HasRun(string fileName, double sheetWidth, double sheetHeight, double spacing)
        {
            return _db.Runs.Any(r =>
                r.Part.FileName == fileName &&
                r.SheetWidth == sheetWidth &&
                r.SheetHeight == sheetHeight &&
                r.Spacing == spacing);
        }

        public int RunCount(string fileName)
        {
            return _db.Runs.Count(r => r.Part.FileName == fileName);
        }

        public void AddRun(long partId, double w, double h, double s, BruteForceResult result, string filePath, List<AngleResult> angleResults = null)
        {
            var run = new TrainingRun
            {
                PartId = partId,
                SheetWidth = w,
                SheetHeight = h,
                Spacing = s,
                PartCount = result.PartCount,
                Utilization = result.Utilization,
                TimeMs = result.TimeMs,
                LayoutData = result.LayoutData ?? "",
                FilePath = filePath ?? "",
                WinnerEngine = result.WinnerEngine ?? "",
                WinnerTimeMs = result.WinnerTimeMs,
                RunnerUpEngine = result.RunnerUpEngine ?? "",
                RunnerUpPartCount = result.RunnerUpPartCount,
                RunnerUpTimeMs = result.RunnerUpTimeMs,
                ThirdPlaceEngine = result.ThirdPlaceEngine ?? "",
                ThirdPlacePartCount = result.ThirdPlacePartCount,
                ThirdPlaceTimeMs = result.ThirdPlaceTimeMs
            };

            _db.Runs.Add(run);

            if (angleResults != null && angleResults.Count > 0)
            {
                foreach (var ar in angleResults)
                {
                    _db.AngleResults.Add(new Data.TrainingAngleResult
                    {
                        Run = run,
                        AngleDeg = ar.AngleDeg,
                        Direction = ar.Direction.ToString(),
                        PartCount = ar.PartCount
                    });
                }
            }

            _db.SaveChanges();
        }

        public int BackfillPerimeterToAreaRatio()
        {
            var partsToFix = _db.Parts
                .Where(p => p.PerimeterToAreaRatio == 0)
                .Select(p => new { p.Id, p.GeometryData })
                .ToList();

            if (partsToFix.Count == 0) return 0;

            var updated = 0;
            foreach (var item in partsToFix)
            {
                try
                {
                    var stream = new MemoryStream(Encoding.UTF8.GetBytes(item.GeometryData));
                    var programReader = new ProgramReader(stream);
                    var program = programReader.Read();

                    var drawing = new Drawing("backfill") { Program = program };
                    drawing.UpdateArea();

                    var features = FeatureExtractor.Extract(drawing);
                    if (features == null) continue;

                    var part = _db.Parts.Find(item.Id);
                    part.PerimeterToAreaRatio = features.PerimeterToAreaRatio;
                    _db.SaveChanges();
                    updated++;
                }
                catch
                {
                    // Skip parts that fail to reconstruct.
                }
            }

            return updated;
        }

        private void MigrateSchema()
        {
            var columns = new[]
            {
                ("WinnerEngine", "TEXT NOT NULL DEFAULT ''"),
                ("WinnerTimeMs", "INTEGER NOT NULL DEFAULT 0"),
                ("RunnerUpEngine", "TEXT NOT NULL DEFAULT ''"),
                ("RunnerUpPartCount", "INTEGER NOT NULL DEFAULT 0"),
                ("RunnerUpTimeMs", "INTEGER NOT NULL DEFAULT 0"),
                ("ThirdPlaceEngine", "TEXT NOT NULL DEFAULT ''"),
                ("ThirdPlacePartCount", "INTEGER NOT NULL DEFAULT 0"),
                ("ThirdPlaceTimeMs", "INTEGER NOT NULL DEFAULT 0"),
            };

            foreach (var (name, type) in columns)
            {
                try
                {
                    _db.Database.ExecuteSqlRaw($"ALTER TABLE Runs ADD COLUMN {name} {type}");
                }
                catch
                {
                    // Column already exists.
                }
            }

            try
            {
                _db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS AngleResults (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        RunId INTEGER NOT NULL,
                        AngleDeg REAL NOT NULL,
                        Direction TEXT NOT NULL,
                        PartCount INTEGER NOT NULL,
                        FOREIGN KEY (RunId) REFERENCES Runs(Id)
                    )");
                _db.Database.ExecuteSqlRaw(
                    "CREATE INDEX IF NOT EXISTS idx_angleresults_runid ON AngleResults (RunId)");
            }
            catch
            {
                // Table already exists or other non-fatal issue.
            }
        }

        public void SaveChanges()
        {
            _db.SaveChanges();
        }

        public void Dispose()
        {
            _db?.Dispose();
        }
    }
}
