using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ACadSharp;
using OpenNest.Bending;
using OpenNest.Geometry;
using OpenNest.IO;
using Xunit;

namespace OpenNest.Tests.IO
{
    public class CadExportRoundTripTests : IDisposable
    {
        private readonly string folder = Directory.CreateTempSubdirectory("cad-export-tests").FullName;

        public void Dispose() => Directory.Delete(folder, true);

        public static IEnumerable<object[]> Formats()
        {
            foreach (var version in new[]
            {
                ACadVersion.AC1032, ACadVersion.AC1027, ACadVersion.AC1024, ACadVersion.AC1021,
                ACadVersion.AC1018, ACadVersion.AC1015, ACadVersion.AC1014,
            })
                yield return new object[] { ".dxf", version };

            foreach (var version in CadExporter.DwgVersions)
                yield return new object[] { ".dwg", version };

            yield return new object[] { ".svg", ACadVersion.AC1032 };
        }

        // A 20 x 10 blank with a hole, an etch layer mark and a second, scribed layer.
        private static List<Entity> Blank(double scale = 1)
        {
            var cut = new Layer("CUT");
            var entities = new List<Entity>
            {
                new Line(new Vector(0, 0), new Vector(20 * scale, 0)),
                new Line(new Vector(20 * scale, 0), new Vector(20 * scale, 10 * scale)),
                new Line(new Vector(20 * scale, 10 * scale), new Vector(0, 10 * scale)),
                new Line(new Vector(0, 10 * scale), new Vector(0, 0)),
                new Circle(new Vector(10 * scale, 5 * scale), 1 * scale),
            };
            foreach (var entity in entities)
                entity.Layer = cut;
            return entities;
        }

        private static List<Bend> Bends(double scale = 1) =>
            new List<Bend>
            {
                // No note: export builds one from the direction, angle and radius.
                new Bend
                {
                    StartPoint = new Vector(5 * scale, 0),
                    EndPoint = new Vector(5 * scale, 10 * scale),
                    Direction = BendDirection.Up,
                    Angle = 90,
                    Radius = 0.06 * scale,
                },
                // Drawn right to left, with its source note kept as written.
                new Bend
                {
                    StartPoint = new Vector(16 * scale, 10 * scale),
                    EndPoint = new Vector(16 * scale, 0),
                    Direction = BendDirection.Down,
                    Angle = 45,
                    Radius = 0.03 * scale,
                    NoteText = "DOWN 45° R 0.03",
                },
            };

        private string Export(string extension, ACadVersion version, List<Entity> entities, List<Bend> bends, Units units)
        {
            var path = Path.Combine(folder, $"part-{version}{extension}");
            var withEtch = entities.ToList();
            Bend.UpdateEtchEntities(withEtch, bends);
            CadExporter.Export(path, withEtch, bends, units, version);
            return path;
        }

        [Theory]
        [MemberData(nameof(Formats))]
        public void BendsAndGeometryReadBack(string extension, ACadVersion version)
        {
            var bends = Bends();
            var path = Export(extension, version, Blank(), bends, Units.Inches);

            var result = CadImporter.Import(path, new CadImportOptions { DrawingUnits = Units.Inches });

            Assert.Equal(bends.Count, result.Bends.Count);
            foreach (var expected in bends)
            {
                var actual = Assert.Single(result.Bends, b => SameLine(b, expected));
                Assert.Equal(expected.Direction, actual.Direction);
                Assert.Equal(expected.Angle.Value, actual.Angle.Value, 6);
                Assert.Equal(expected.Radius.Value, actual.Radius.Value, 6);
            }
            Assert.Equal("DOWN 45° R 0.03", result.Bends.Single(b => b.Direction == BendDirection.Down).NoteText);

            // Etch marks are dropped and made again from the bends, never read as cut geometry.
            var cut = result.Entities.Where(e => e.Layer?.Name != Bend.EtchLayer.Name).ToList();
            Assert.Equal(4, cut.OfType<Line>().Count());
            Assert.Single(cut.OfType<Circle>());
            Assert.All(cut, e => Assert.Equal("CUT", e.Layer.Name));
            Assert.Equal(2, result.Entities.Count(e => e.Layer?.Name == Bend.EtchLayer.Name));
        }

        [Theory]
        [InlineData(".dxf")]
        [InlineData(".dwg")]
        [InlineData(".svg")]
        public void MillimeterBendsReadBack(string extension)
        {
            var bends = Bends(25.4);
            var path = Export(extension, ACadVersion.AC1032, Blank(25.4), bends, Units.Millimeters);

            var result = CadImporter.Import(path, new CadImportOptions { DrawingUnits = Units.Millimeters });

            Assert.Equal(2, result.Bends.Count);
            Assert.All(bends, expected => Assert.Contains(result.Bends, b => SameLine(b, expected)));
            Assert.Equal(1.524, result.Bends.Single(b => b.Direction == BendDirection.Up).Radius.Value, 6);
        }

        [Fact]
        public void SvgBendsScaleWithTheDrawingUnits()
        {
            var path = Export(".svg", ACadVersion.AC1032, Blank(), Bends(), Units.Inches);

            var result = CadImporter.Import(path, new CadImportOptions { DrawingUnits = Units.Millimeters });

            var up = result.Bends.Single(b => b.Direction == BendDirection.Up);
            Assert.Equal(5 * 25.4, up.StartPoint.X, 6);
            Assert.Equal(10 * 25.4, up.Length, 6);
            Assert.Equal(0.06 * 25.4, up.Radius.Value, 6);
            Assert.Equal(90, up.Angle.Value, 6);
        }

        [Theory]
        [InlineData(".dxf")]
        [InlineData(".dwg")]
        [InlineData(".svg")]
        public void LinePickedAsABendIsWrittenOnlyAsTheBend(string extension)
        {
            var entities = Blank();
            var picked = new Line(new Vector(8, 0), new Vector(8, 10)) { Layer = new Layer("CUT"), IsVisible = false };
            entities.Add(picked);
            var bends = new List<Bend>
            {
                new Bend
                {
                    StartPoint = picked.StartPoint,
                    EndPoint = picked.EndPoint,
                    Direction = BendDirection.Up,
                    Angle = 90,
                    Radius = 0.06,
                    SourceEntity = picked,
                },
            };
            var path = Export(extension, ACadVersion.AC1032, entities, bends, Units.Inches);

            var result = CadImporter.Import(path, new CadImportOptions { DrawingUnits = Units.Inches });

            Assert.Single(result.Bends);
            Assert.Equal(4, result.Entities.OfType<Line>().Count(e => e.Layer?.Name == "CUT"));
        }

        [Theory]
        [InlineData(".dxf")]
        [InlineData(".svg")]
        public void BendWithoutADirectionStaysABend(string extension)
        {
            var bend = new Bend
            {
                StartPoint = new Vector(5, 0),
                EndPoint = new Vector(5, 10),
                Direction = BendDirection.Unknown,
            };
            var path = Export(extension, ACadVersion.AC1032, Blank(), new List<Bend> { bend }, Units.Inches);

            var result = CadImporter.Import(path, new CadImportOptions { DrawingUnits = Units.Inches });

            var actual = Assert.Single(result.Bends);
            Assert.True(SameLine(actual, bend));
            Assert.Equal(BendDirection.Unknown, actual.Direction);
        }

        [Fact]
        public void SvgBendLinesCarryTheirValues()
        {
            var path = Export(".svg", ACadVersion.AC1032, Blank(), Bends(), Units.Inches);

            var svg = XNamespace.Get("http://www.w3.org/2000/svg");
            var group = XDocument.Load(path).Root.Elements(svg + "g")
                .Single(g => (string)g.Attribute("data-layer") == "BEND");
            var up = group.Elements(svg + "line").First();
            Assert.Equal("up", (string)up.Attribute("data-bend-direction"));
            Assert.Equal("90", (string)up.Attribute("data-bend-angle"));
            Assert.Equal("0.06", (string)up.Attribute("data-bend-radius"));
            Assert.Contains(group.Elements(svg + "text"), t => t.Value == "UP 90° R 0.06");
        }

        [Fact]
        public void DwgCannotBeWrittenAs2007()
        {
            var path = Path.Combine(folder, "part.dwg");
            Assert.Throws<NotSupportedException>(
                () => CadExporter.Export(path, Blank(), Bends(), Units.Inches, ACadVersion.AC1021)
            );
        }

        [Fact]
        public void LayerNamesAutoCadRejectsAreReplaced()
        {
            var entities = Blank();
            foreach (var entity in entities)
                entity.Layer = new Layer("Cut: outside");

            var document = CadExporter.BuildDocument(entities, null, Units.Inches);

            Assert.True(document.Layers.Contains("Cut_ outside"));
        }

        private static bool SameLine(Bend a, Bend b) =>
            (a.StartPoint.DistanceTo(b.StartPoint) < 1e-6 && a.EndPoint.DistanceTo(b.EndPoint) < 1e-6)
            || (a.StartPoint.DistanceTo(b.EndPoint) < 1e-6 && a.EndPoint.DistanceTo(b.StartPoint) < 1e-6);
    }
}
