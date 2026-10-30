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
        public void EmptyPlateStillWritesAValidFile()
        {
            var path = PathFor("empty.svg");

            Svg.ExportEntities(Array.Empty<Entity>(), path, Units.Inches);

            Assert.Empty(CadImporter.Import(path).Entities);
        }
    }
}
