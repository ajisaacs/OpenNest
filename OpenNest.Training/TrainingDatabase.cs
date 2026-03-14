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

        public void AddRun(long partId, double w, double h, double s, BruteForceResult result, string filePath)
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
                FilePath = filePath ?? ""
            };

            _db.Runs.Add(run);
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
