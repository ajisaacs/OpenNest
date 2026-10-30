using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;
using Xunit;

namespace OpenNest.Tests.IO
{
    public class SvgImportTests : IDisposable
    {
        private const double Precision = 0.002;
        private const string SquarePath = "<path d='M0 0H10V10H0Z'/>";

        private readonly string folder = Directory.CreateTempSubdirectory("svg-tests").FullName;

        public void Dispose() => Directory.Delete(folder, true);

        private string Write(string content)
        {
            var path = Path.Combine(folder, Guid.NewGuid() + ".svg");
            File.WriteAllText(path, content);
            return path;
        }

        private static string Svg(string body, string attributes = "width='10in' height='10in' viewBox='0 0 10 10'") =>
            $"<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' {attributes}>{body}</svg>";

        private CadImportResult Import(string content, CadImportOptions options = null) =>
            CadImporter.Import(Write(content), options ?? new CadImportOptions());

        private static (double Length, double Width) Size(CadImportResult result) =>
            (result.Bounds.Length, result.Bounds.Width);

        [Theory]
        [InlineData("width='10in' height='10in' viewBox='0 0 100 100'", 1.0)]
        [InlineData("width='254mm' height='254mm' viewBox='0 0 10 10'", 10.0)]
        [InlineData("width='25.4cm' height='25.4cm' viewBox='0 0 10 10'", 10.0)]
        [InlineData("width='720pt' height='720pt' viewBox='0 0 10 10'", 10.0)]
        [InlineData("width='60pc' height='60pc' viewBox='0 0 10 10'", 10.0)]
        // Without a view box user units are CSS pixels, whatever unit the page is in.
        [InlineData("width='1in' height='1in'", 10.0 / 96)]
        public void DeclaredSizeGivesRealWorldScale(string attributes, double expected)
        {
            var (length, width) = Size(Import(Svg(SquarePath, attributes)));

            Assert.Equal(expected, length, 6);
            Assert.Equal(expected, width, 6);
        }

        [Fact]
        public void ConvertsToMillimeterDrawings()
        {
            var result = Import(Svg(SquarePath), new CadImportOptions { DrawingUnits = Units.Millimeters });

            Assert.Equal(254, result.Bounds.Length, 6);
        }

        [Theory]
        [InlineData("viewBox='0 0 10 10'")]
        [InlineData("width='100%' height='100%' viewBox='0 0 10 10'")]
        [InlineData("width='100px' height='100px' viewBox='0 0 10 10'")]
        [InlineData("width='100' height='100' viewBox='0 0 10 10'")]
        [InlineData("")]
        public void UndeclaredSizeRefusesToGuess(string attributes)
        {
            var path = Write(Svg(SquarePath, attributes));

            var error = Assert.Throws<InvalidDataException>(() => CadImporter.Import(path));
            Assert.Contains("real-world size", error.Message);
            Assert.Equal((10, 10), OpenNest.IO.Svg.UndeclaredSize(path));
        }

        [Theory]
        [InlineData(96, 10.0 / 96)]
        [InlineData(72, 10.0 / 72)]
        [InlineData(1, 10.0)]
        public void ExplicitScaleAppliesToUndeclaredFiles(double unitsPerInch, double expected)
        {
            var result = Import(
                Svg(SquarePath, "viewBox='0 0 10 10'"),
                new CadImportOptions { SvgUserUnitsPerInch = unitsPerInch }
            );

            Assert.Equal(expected, result.Bounds.Length, 6);
        }

        [Fact]
        public void DeclaredSizeBeatsExplicitScale()
        {
            var result = Import(Svg(SquarePath), new CadImportOptions { SvgUserUnitsPerInch = 96 });

            Assert.Equal(10, result.Bounds.Length, 6);
            Assert.Null(OpenNest.IO.Svg.UndeclaredSize(Write(Svg(SquarePath))));
        }

        [Fact]
        public void NonUniformPageFitsViewBoxUniformly()
        {
            var result = Import(Svg(SquarePath, "width='20in' height='10in' viewBox='0 0 10 10'"));

            Assert.Equal((10, 10), Size(result));
        }

        [Fact]
        public void PreserveAspectRatioNoneStretches()
        {
            var result = Import(Svg(
                SquarePath,
                "width='20in' height='10in' viewBox='0 0 10 10' preserveAspectRatio='none'"
            ));

            Assert.Equal((20, 10), Size(result));
        }

        [Fact]
        public void FlipsYSoTheDrawingIsNotMirrored()
        {
            var result = Import(Svg("<path d='M0 0L4 0L0 2Z'/>"));

            // The wide edge is at the top of the page, so it is the high Y edge in OpenNest.
            var top = result.Entities.OfType<Line>().Single(l =>
                System.Math.Abs(l.StartPoint.Y - l.EndPoint.Y) < 1e-9
            );
            Assert.Equal(result.Bounds.Top, top.StartPoint.Y, 6);
        }

        [Fact]
        public void BasicShapesImport()
        {
            var result = Import(Svg(
                "<rect x='1' y='1' width='4' height='2'/>"
                + "<circle cx='2' cy='2' r='1'/>"
                + "<line x1='10' y1='10' x2='11' y2='11'/>"
                + "<polyline points='20,20 21,20 21,21'/>"
                + "<polygon points='30 30 31 30 31 31'/>"
            ));

            Assert.Single(result.Entities.OfType<Circle>());
            Assert.Equal(1, result.Entities.OfType<Circle>().Single().Radius, 9);
            Assert.Equal(10, result.Entities.OfType<Line>().Count());
        }

        [Fact]
        public void RoundedRectKeepsItsCorners()
        {
            var result = Import(Svg("<rect width='10' height='6' rx='1'/>"));

            Assert.Equal(4, result.Entities.OfType<Line>().Count());
            var corners = result.Entities.OfType<Arc>().ToList();
            Assert.Equal(4, corners.Count);
            Assert.All(corners, a => Assert.Equal(1, a.Radius, 6));
            Assert.Equal((10, 6), Size(result));
            AssertClosed(result.Entities);
        }

        [Fact]
        public void AttributeUnitsResolveAgainstCssPixels()
        {
            // Inside a 96-per-inch page, 1in is 96 user units: one real inch.
            var result = Import(Svg(
                "<circle r='1in'/>",
                "width='100in' height='100in' viewBox='0 0 9600 9600'"
            ));

            Assert.Equal(1, result.Entities.OfType<Circle>().Single().Radius, 6);
        }

        [Theory]
        [InlineData("M0 0 10 0 10 10z", 3)]
        [InlineData("m0 0 10 0 0 10z", 3)]
        [InlineData("M0,0L10,0L10,10Z", 3)]
        [InlineData("M0 0h10v10h-10", 3)]
        [InlineData("M0-1.5.5.5", 1)]
        public void PathGrammar(string data, int lines)
        {
            Assert.Equal(lines, Import(Svg($"<path d='{data}'/>")).Entities.OfType<Line>().Count());
        }

        [Fact]
        public void ImplicitLineAfterRelativeMoveIsRelative()
        {
            var result = Import(Svg("<path d='m1 1 2 0 0 2'/>"));

            Assert.Equal((2, 2), Size(result));
        }

        [Fact]
        public void CompactArcFlagsParse()
        {
            var result = Import(Svg("<path d='M0 0a1 1 0 011 1'/>"));

            Assert.Equal(1, result.Entities.OfType<Arc>().Single().Radius, 6);
        }

        [Theory]
        [InlineData("L1 1")]
        [InlineData("M0 0L1")]
        [InlineData("M0 0Q1 1")]
        [InlineData("M0 0A1 1 0 2 0 1 1")]
        [InlineData("M0 0Y1 1")]
        public void MalformedPathIsRejectedRatherThanPartlyImported(string data)
        {
            var path = Write(Svg($"<path d='{data}'/>"));

            Assert.Throws<InvalidDataException>(() => CadImporter.Import(path));
        }

        [Fact]
        public void CircularArcKeepsItsSweepDirection()
        {
            // Sweep 1 from (0,0) to (2,0) passes through y=-1 on the page, so y=+1 in OpenNest.
            var result = Import(Svg("<path d='M0 0A1 1 0 0 1 2 0'/>"));

            var arc = result.Entities.OfType<Arc>().Single();
            Assert.Equal(1, arc.Radius, 6);
            Assert.Equal(1, result.Bounds.Top, 6);
            Assert.Equal(0, result.Bounds.Bottom, 6);
        }

        [Fact]
        public void LargeArcFlagTakesTheLongWay()
        {
            var result = Import(Svg("<path d='M0 0A5 5 0 1 1 6 0'/>"));

            Assert.True(result.Entities.OfType<Arc>().Single().SweepAngle() > System.Math.PI);
        }

        [Fact]
        public void TooSmallArcRadiusScalesUp()
        {
            var result = Import(Svg("<path d='M0 0A1 1 0 0 1 10 0'/>"));

            Assert.Equal(5, result.Entities.OfType<Arc>().Single().Radius, 6);
        }

        [Fact]
        public void TwoHalfArcsMakeAWholeHole()
        {
            var result = Import(Svg("<path d='M0 5A5 5 0 0 1 10 5A5 5 0 0 1 0 5Z'/>"));

            Assert.Equal((10, 10), Size(result));
            AssertArea(result, System.Math.PI * 25);
        }

        [Fact]
        public void IllustratorStyleBezierCircleFitsTheCircle()
        {
            const double k = 0.5522847498 * 5;
            var data = FormattableString.Invariant(
                $"M5 0C{5 + k} 0 10 {5 - k} 10 5S{5 + k} 10 5 10S0 {5 + k} 0 5S{5 - k} 0 5 0Z"
            );

            var result = Import(Svg($"<path d='{data}'/>"));

            Assert.Equal(10, result.Bounds.Length, 2);
            AssertClosed(result.Entities);
            AssertArea(result, System.Math.PI * 25);
        }

        [Fact]
        public void QuadraticAndSmoothQuadraticStayOnTheCurve()
        {
            var result = Import(Svg("<path d='M0 0Q5 10 10 0T20 0'/>"));

            Assert.Equal(20, result.Bounds.Length, 3);
            Assert.Equal(10, result.Bounds.Width, 2);
        }

        [Fact]
        public void TransformsNestInOrder()
        {
            var result = Import(Svg(
                "<g transform='translate(100 0)'><g transform='scale(2)'>"
                + "<rect x='1' width='1' height='1'/></g></g>"
            ));

            Assert.Equal(102, result.Bounds.Left, 6);
            Assert.Equal(2, result.Bounds.Length, 6);
        }

        [Fact]
        public void RotateAboutPointKeepsThePoint()
        {
            var result = Import(Svg("<rect width='2' height='2' transform='rotate(90 1 1)'/>"));

            Assert.Equal(0, result.Bounds.Left, 6);
            Assert.Equal(2, result.Bounds.Right, 6);
        }

        [Fact]
        public void UniformScaleKeepsACircle()
        {
            var result = Import(Svg("<circle r='2' transform='rotate(30) scale(3)'/>"));

            Assert.Equal(6, result.Entities.OfType<Circle>().Single().Radius, 6);
        }

        [Fact]
        public void NonUniformScaleMakesACircleAnEllipse()
        {
            var result = Import(Svg("<circle r='5' transform='scale(2 1)'/>"));

            Assert.Empty(result.Entities.OfType<Circle>());
            Assert.Equal(20, result.Bounds.Length, 2);
            Assert.Equal(10, result.Bounds.Width, 2);
            AssertArea(result, System.Math.PI * 50);
        }

        [Fact]
        public void RotatedEllipseKeepsItsAxes()
        {
            var result = Import(Svg("<ellipse rx='6' ry='3' transform='rotate(90)'/>"));

            Assert.Equal(6, result.Bounds.Length, 2);
            Assert.Equal(12, result.Bounds.Width, 2);
        }

        [Fact]
        public void EllipticalArcEndsWhereItsNeighborsStart()
        {
            var result = Import(Svg("<path d='M0 0H4A4 2 30 0 1 8 3V8H0Z'/>"));

            AssertClosed(result.Entities);
        }

        [Fact]
        public void HiddenAndDefinitionContentIsIgnored()
        {
            var result = Import(Svg(
                "<defs><circle id='c' r='1'/></defs>"
                + "<g display='none'><rect width='100' height='100'/></g>"
                + "<rect width='100' height='100' style='fill:red;display:none'/>"
                + "<rect width='100' height='100' visibility='hidden'/>"
                + "<clipPath><rect width='100' height='100'/></clipPath>"
                + SquarePath
            ));

            Assert.Equal((10, 10), Size(result));
            Assert.Empty(result.Warnings);
        }

        [Fact]
        public void UseCopiesTheTargetAtItsOffset()
        {
            var result = Import(Svg(
                "<defs><rect id='r' width='2' height='2'/></defs>"
                + "<use xlink:href='#r' x='5'/><use href='#r' x='10'/>"
            ));

            Assert.Equal(5, result.Bounds.Left, 6);
            Assert.Equal(12, result.Bounds.Right, 6);
        }

        [Fact]
        public void SelfReferencingUseDoesNotHang()
        {
            var result = Import(Svg("<g id='a'><use href='#a'/></g>" + SquarePath));

            Assert.Equal((10, 10), Size(result));
            Assert.Contains(result.Warnings, w => w.Contains("<use>"));
        }

        [Fact]
        public void UnsupportedContentIsReported()
        {
            var result = Import(Svg("<text>hello</text><text>x</text><image/>" + SquarePath));

            Assert.Contains(result.Warnings, w => w.Contains("2 <text>"));
            Assert.Contains(result.Warnings, w => w.Contains("1 <image>"));
        }

        [Fact]
        public void LayersComeFromGroups()
        {
            var result = Import(Svg(
                "<g data-layer='One'><rect width='1' height='1'/></g>"
                + "<g id='Two'><rect x='2' width='1' height='1'/></g>"
                + "<g xmlns:i='http://www.inkscape.org/namespaces/inkscape' i:groupmode='layer' i:label='Three'>"
                + "<rect x='4' width='1' height='1'/></g>"
            ));

            var names = result.Entities.Select(e => e.Layer.Name).Distinct().OrderBy(n => n);
            Assert.Equal(new[] { "One", "Three", "Two" }, names);
            Assert.All(result.Entities, e => Assert.True(e.Layer.IsVisible));
        }

        [Fact]
        public void NonCutLayersAreDropped()
        {
            var result = Import(Svg(
                "<g data-layer='BEND'><rect width='100' height='1'/></g>"
                + "<g data-layer='etch'><rect width='100' height='1'/></g>"
                + "<g data-layer='RAPID'><rect width='100' height='1'/></g>"
                + "<g data-layer='PLATE'><rect width='100' height='1'/></g>"
                + SquarePath
            ));

            Assert.Equal((10, 10), Size(result));
        }

        [Fact]
        public void ImportedDrawingIsStableAcrossImports()
        {
            var path = Write(Svg(SquarePath));

            var first = CadImporter.Import(path);
            var second = CadImporter.Import(path);

            Assert.Equal(first.Entities.Select(e => e.Id), second.Entities.Select(e => e.Id));
        }

        [Fact]
        public void BuildsADrawingWithAProgram()
        {
            var drawing = CadImporter.ImportDrawing(Write(Svg(SquarePath)));

            Assert.NotEmpty(drawing.Program.Codes);
            Assert.Equal(100, drawing.Area, 6);
            Assert.Empty(drawing.SuppressedEntityIds);
        }

        [Fact]
        public void IllustratorEntityNamespacesParse()
        {
            var path = Write(
                "<!DOCTYPE svg [<!ENTITY ns_svg \"http://www.w3.org/2000/svg\">]>"
                + "<svg xmlns='&ns_svg;' width='10in' height='10in' viewBox='0 0 10 10'>"
                + SquarePath
                + "</svg>"
            );

            Assert.Equal(10, CadImporter.Import(path).Bounds.Length, 6);
        }

        [Fact]
        public void EntityExpansionBombIsRejected()
        {
            var entities = "<!ENTITY a \"aaaaaaaaaa\">";
            var previous = "a";
            for (var i = 0; i < 9; i++)
            {
                var name = "e" + i;
                var refs = string.Concat(Enumerable.Repeat($"&{previous};", 10));
                entities += $"<!ENTITY {name} \"{refs}\">";
                previous = name;
            }

            var path = Write($"<!DOCTYPE svg [{entities}]><svg><title>&{previous};</title></svg>");

            Assert.Throws<InvalidDataException>(() => CadImporter.Import(path));
        }

        [Fact]
        public void OtherXmlIsNotAnSvg()
        {
            var path = Write("<html/>");

            Assert.Throws<InvalidDataException>(() => CadImporter.Import(path));
        }

        private static void AssertClosed(IEnumerable<Entity> entities)
        {
            var shapes = ShapeBuilder.GetShapes(entities.ToList());
            Assert.Single(shapes);
            Assert.True(shapes[0].IsClosed());
        }

        private static void AssertArea(CadImportResult result, double expected)
        {
            var drawing = CadImporter.BuildDrawing(result, result.Entities, result.Bends, 1, null, null);
            Assert.Equal(expected, drawing.Area, 0.1);
        }
    }
}
