using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;
using Xunit;

namespace OpenNest.Tests.IO
{
    public class SvgExportTests : IDisposable
    {
        private readonly string folder = Directory.CreateTempSubdirectory("svg-export-tests").FullName;

        public void Dispose() => Directory.Delete(folder, true);

        private string PathFor(string name) => Path.Combine(folder, name);

        private static Drawing Plated()
        {
            // A 10 x 4 slot: lines and two half circles, with a hole.
            var entities = new Entity[]
            {
                new Line(new Vector(2, 0), new Vector(8, 0)),
                new Arc(new Vector(8, 2), 2, -System.Math.PI / 2, System.Math.PI / 2),
                new Line(new Vector(8, 4), new Vector(2, 4)),
                new Arc(new Vector(2, 2), 2, System.Math.PI / 2, -System.Math.PI / 2),
                new Circle(new Vector(5, 2), 1),
            };
            foreach (var entity in entities)
                entity.Layer = Layer.Default;

            return new Drawing("slot", ConvertGeometry.ToProgram(entities.ToList()));
        }

        [Theory]
        [InlineData(Units.Inches)]
        [InlineData(Units.Millimeters)]
        public void ProgramRoundTripsAtTheSameSizeAndArea(Units units)
        {
            var drawing = Plated();
            var path = PathFor("slot.svg");

            Svg.ExportProgram(drawing.Program, path, units);
            var imported = CadImporter.ImportDrawing(path, new CadImportOptions { DrawingUnits = units });

            var original = drawing.Program.BoundingBox();
            var box = imported.Program.BoundingBox();
            Assert.Equal(original.Length, box.Length, 6);
            Assert.Equal(original.Width, box.Width, 6);
            Assert.Equal(drawing.Area, imported.Area, 6);
        }

        [Fact]
        public void ExportStatesPhysicalSizeSoNoScaleIsNeeded()
        {
            var path = PathFor("slot.svg");

            Svg.ExportProgram(Plated().Program, path, Units.Millimeters);

            Assert.Null(Svg.UndeclaredSize(path));
            Assert.EndsWith("mm", XDocument.Load(path).Root.Attribute("width").Value);
        }

        [Fact]
        public void InchExportReadsAtMillimeters()
        {
            var path = PathFor("slot.svg");

            Svg.ExportProgram(Plated().Program, path, Units.Inches);
            var imported = CadImporter.Import(path, new CadImportOptions { DrawingUnits = Units.Millimeters });

            Assert.Equal(254, imported.Bounds.Length, 6);
        }

        [Fact]
        public void ExportUsesInvariantNumbers()
        {
            var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            try
            {
                var path = PathFor("slot.svg");
                Svg.ExportEntities(new[] { new Line(0.5, 0.5, 1.5, 0.5) }, path, Units.Inches);

                Assert.DoesNotContain("0,5", File.ReadAllText(path));
                Assert.Equal(1, CadImporter.Import(path).Bounds.Length, 6);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        [Fact]
        public void ReversedArcRoundTripsOnItsOwnSide()
        {
            var arc = new Arc(new Vector(0, 0), 1, System.Math.PI, 0, true);
            var path = PathFor("arc.svg");

            Svg.ExportEntities(new[] { arc }, path, Units.Inches);
            var bounds = CadImporter.Import(path).Bounds;

            // Clockwise from 180 to 0 degrees is the top half.
            Assert.Equal(1, bounds.Top, 6);
            Assert.Equal(0, bounds.Bottom, 6);
        }

        [Fact]
        public void PlateExportPlacesPartsAndKeepsThemOnReimport()
        {
            var drawing = Plated();
            var plate = new Plate(100, 50) { Quadrant = 1 };
            var part = new Part(drawing);
            part.Location = new Vector(20, 30);
            plate.Parts.Add(part);
            var path = PathFor("plate.svg");

            Svg.ExportPlate(plate, path, Units.Inches);

            var root = XDocument.Load(path).Root;
            var layers = root.Elements().Select(g => g.Attribute("data-layer").Value).ToList();
            Assert.Contains("PLATE", layers);
            Assert.Contains("RAPID", layers);

            // The plate and rapids are not part geometry.
            var imported = CadImporter.Import(path).Bounds;
            var expected = part.BoundingBox;
            Assert.Equal(expected.Left, imported.Left, 6);
            Assert.Equal(expected.Bottom, imported.Bottom, 6);
            Assert.Equal(expected.Length, imported.Length, 6);
        }

        [Fact]
        public void LaserExportKeepsOnlyCutsAtTheSameSize()
        {
            var drawing = Plated();
            var plate = new Plate(100, 50) { Quadrant = 1 };
            plate.Parts.Add(new Part(drawing) { Location = new Vector(20, 30) });
            plate.Parts.Add(new Part(drawing) { Location = new Vector(40, 30) });
            var path = PathFor("laser.svg");

            Svg.ExportPlate(plate, path, Units.Inches, SvgExportOptions.LaserSoftware);

            var root = XDocument.Load(path).Root;
            var layers = root.Elements().Select(g => g.Attribute("data-layer").Value).ToList();
            Assert.DoesNotContain("PLATE", layers);
            Assert.DoesNotContain("RAPID", layers);
            Assert.Null(Svg.UndeclaredSize(path));

            var imported = CadImporter.Import(path).Bounds;
            var boxes = plate.Parts.Select(p => p.BoundingBox).ToList();
            var left = boxes.Min(b => b.Left);
            var bottom = boxes.Min(b => b.Bottom);
            Assert.Equal(left, imported.Left, 6);
            Assert.Equal(bottom, imported.Bottom, 6);
            Assert.Equal(boxes.Max(b => b.Left + b.Length) - left, imported.Length, 6);
            Assert.Equal(boxes.Max(b => b.Bottom + b.Width) - bottom, imported.Width, 6);
        }

        [Fact]
        public void LaserColorsPutEachKindOfCutOnItsOwnLayerColor()
        {
            Entity On(Entity entity, Layer layer)
            {
                entity.Layer = layer;
                return entity;
            }

            var entities = new[]
            {
                On(new Line(0, 0, 1, 0), SpecialLayers.Cut),
                On(new Line(0, 0, 0, 1), SpecialLayers.Leadin),
                On(new Line(1, 0, 1, 1), SpecialLayers.Scribe),
                On(new Line(0, 1, 1, 1), SpecialLayers.Break),
                On(new Line(0, 2, 1, 2), new Layer("ENGRAVE")),
                On(new Line(0, 3, 1, 3), new Layer("OTHER")),
                On(new Line(0, 4, 1, 4), SpecialLayers.Tab),
            };
            var path = PathFor("colors.svg");

            using (var stream = File.Create(path))
                Svg.ExportEntities(entities, null, stream, Units.Inches, SvgExportOptions.LaserSoftware);

            var strokes = XDocument.Load(path).Root.Elements()
                .ToDictionary(g => g.Attribute("data-layer").Value, g => g.Attribute("stroke").Value);
            Assert.Equal("#000000", strokes["CUT"]);
            Assert.Equal("#000000", strokes["LEADIN"]);
            Assert.Equal("#0000FF", strokes["SCRIBE"]);
            Assert.Equal("#FF0000", strokes["BREAK"]);
            Assert.Equal("#00E000", strokes["ENGRAVE"]);
            Assert.Equal("#D0D000", strokes["OTHER"]);
            // A tabbed cutout's tabs are never cut.
            Assert.False(strokes.ContainsKey("TAB"));
        }

        [Fact]
        public void DefaultExportKeepsEveryLayerInOpenNestColors()
        {
            var entities = new Entity[] { new Line(0, 0, 1, 0), new Line(0, 1, 1, 1) };
            entities[0].Layer = SpecialLayers.Cut;
            entities[1].Layer = SpecialLayers.Tab;
            var path = PathFor("default.svg");

            Svg.ExportEntities(entities, path, Units.Inches);

            var strokes = XDocument.Load(path).Root.Elements()
                .ToDictionary(g => g.Attribute("data-layer").Value, g => g.Attribute("stroke").Value);
            Assert.Equal("#000000", strokes["CUT"]);
            Assert.Equal("#000000", strokes["TAB"]);
        }

        [Fact]
        public void EmptyPlateStillWritesAValidFile()
        {
            var path = PathFor("empty.svg");

            Svg.ExportEntities(Array.Empty<Entity>(), path, Units.Inches);

            Assert.Empty(CadImporter.Import(path).Entities);
        }
    }
}
