using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Tests.Geometry;

public class PartGeometryDirectionalPreparationTests
{
    [Theory]
    [InlineData(PushDirection.Left, false, -1, 0)]
    [InlineData(PushDirection.Right, false, 1, 0)]
    [InlineData(PushDirection.Up, false, 0, 1)]
    [InlineData(PushDirection.Down, false, 0, -1)]
    [InlineData(PushDirection.Left, true, -1, 0)]
    [InlineData(PushDirection.Right, true, 1, 0)]
    [InlineData(PushDirection.Up, true, 0, 1)]
    [InlineData(PushDirection.Down, true, 0, -1)]
    public void Cardinals_PreserveOrderedEndpointsAndWinding(
        PushDirection direction, bool reverse, double x, double y)
    {
        var part = MakePart(RectangleProgram(reverse));
        var expected = FrozenGetPartLines(part, direction);
        Assert.Single(expected);
        AssertMatches(part, direction, new Vector(x, y), 0.001);
        AssertLines(expected, FrozenGetPartLines(part, new Vector(x, y)));
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.001)]
    [InlineData(0.00001)]
    public void CurvedPerimeterAndMultipleHoles_PreserveTessellationAndShapeOrder(double tolerance)
    {
        var program = CurvedProgram();
        program.Mode = Mode.Incremental;
        var part = MakePart(program);
        var expected = FrozenGetPartLines(part, PushDirection.Right, tolerance);
        Assert.True(expected.Count > 3);
        AssertMatches(part, PushDirection.Right, new Vector(2.75, -0.625), tolerance);
        // The non-directional overload is deliberately outside this extraction.
        AssertLines(FrozenGetPartLines(part, (PushDirection)123, tolerance),
            PartGeometry.GetPartLines(part, tolerance));
        Assert.True(FrozenGetPartLines(part, PushDirection.Right, 0.00001).Count
            > FrozenGetPartLines(part, PushDirection.Right, 0.1).Count);
    }

    [Fact]
    public void MaterialIncludesDisplayAndLeads_ButExcludesRapidAndScribe()
    {
        var material = RectangleProgram(false);
        AddRectangle(material, 15, 2, 3, 2, LayerType.Display);
        AddRectangle(material, 21, 3, 2, 4, LayerType.Leadin);
        AddRectangle(material, 26, 1, 4, 3, LayerType.Leadout);
        var clean = MakePart(material);
        AddRectangle(material, 100, 120, 7, 9, LayerType.Scribe);
        material.MoveTo(-200, -300);
        material.MoveTo(400, 500);
        var marked = MakePart(material);
        foreach (var direction in new[] { PushDirection.Left, PushDirection.Right, PushDirection.Up, PushDirection.Down })
        {
            var expected = FrozenGetPartLines(clean, direction);
            Assert.Equal(4, expected.Count);
            AssertLines(expected, PartGeometry.GetPartLines(marked, direction));
            AssertMatches(marked, direction, new Vector(3, -2), 0.001);
        }
        AssertLines(PartGeometry.GetPartLines(clean), PartGeometry.GetPartLines(marked));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(123)]
    public void InvalidEnum_KeepsAllEdgesRatherThanUsingZeroVector(int value)
    {
        var part = MakePart(RectangleProgram(false));
        var direction = (PushDirection)value;
        Assert.Equal(4, FrozenGetPartLines(part, direction).Count);
        Assert.Empty(FrozenGetPartLines(part, new Vector(0, 0)));
        AssertMatches(part, direction, new Vector(0, 0), 0.001);
        AssertLines(PartGeometry.GetPartLines(part), PartGeometry.GetPartLines(part, direction));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2.75, -0.625)]
    [InlineData(-19, 7)]
    [InlineData(double.Epsilon, -double.Epsilon)]
    [InlineData(double.MaxValue, double.MaxValue)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(0, double.NegativeInfinity)]
    [InlineData(double.NaN, 1)]
    public void Vectors_PreserveUnnormalizedZeroAndNonfiniteArithmetic(double x, double y)
    {
        var part = MakePart(RectangleProgram(true));
        var direction = new Vector(x, y);
        AssertMatches(part, PushDirection.Down, direction, 0.001);
        if (x == 0 && y == 0 || double.IsNaN(x))
            Assert.Empty(PartGeometry.GetPartLines(part, direction));
    }

    [Theory]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(0, double.NegativeInfinity)]
    [InlineData(double.NaN, 1)]
    public void NonfiniteTranslation_PreservesDistinctCardinalAndVectorFilters(double x, double y)
    {
        var part = MakePart(RectangleProgram(false));
        part.Location = new Vector(x, y);
        AssertMatches(part, PushDirection.Right, new Vector(1, 0), 0.001);
        AssertMatches(part, (PushDirection)123, new Vector(0, 0), 0.001);
        if (double.IsPositiveInfinity(x))
        {
            Assert.Single(PartGeometry.GetPartLines(part, PushDirection.Right));
            // Infinite X makes edx NaN; multiplying that by a zero vector component
            // is not equivalent to the enum filter, which reads only dy here.
            Assert.Empty(PartGeometry.GetPartLines(part, new Vector(1, 0)));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EmptyAndDegenerateContours_PreserveFallback(int points)
    {
        var program = new Program();
        if (points > 0)
        {
            program.MoveTo(1, 2);
            program.LineTo(points == 1 ? 1 : 5, points == 1 ? 2 : 3);
        }
        var part = MakePart(program);
        AssertMatches(part, (PushDirection)123, new Vector(0, 0), 0.001);
        AssertMatches(part, PushDirection.Up, new Vector(double.NaN, 0), 0.001);
        AssertLines(FrozenGetPartLines(part, (PushDirection)123), PartGeometry.GetPartLines(part));
    }

    [Fact]
    public void NullPartAndMalformedProgram_PreserveExceptions()
    {
        Assert.Throws<NullReferenceException>(() => FrozenGetPartLines(null!, PushDirection.Right));
        Assert.Throws<NullReferenceException>(() => PartGeometry.GetPartLines(null!, PushDirection.Right));
        Assert.Throws<NullReferenceException>(() => FrozenGetPartLines(null!, new Vector(1, 0)));
        Assert.Throws<NullReferenceException>(() => PartGeometry.GetPartLines(null!, new Vector(1, 0)));
        var part = MakePart(RectangleProgram(false));
        part.Program.Codes.Add(null!);
        Assert.Throws<NullReferenceException>(() => FrozenGetPartLines(part, PushDirection.Right));
        Assert.Throws<NullReferenceException>(() => PartGeometry.GetPartLines(part, PushDirection.Right));
        Assert.Throws<NullReferenceException>(() => FrozenGetPartLines(part, new Vector(1, 0)));
        Assert.Throws<NullReferenceException>(() => PartGeometry.GetPartLines(part, new Vector(1, 0)));
    }

    private static void AssertMatches(Part part, PushDirection cardinal, Vector vector, double tolerance)
    {
        var program = part.Program;
        var codes = program.Codes;
        var identities = codes.ToArray();
        var values = ProgramBits(program);
        var drawingValues = ProgramBits(part.BaseDrawing.Program);
        var location = new[] { Bits(part.Location.X), Bits(part.Location.Y) };
        AssertLines(FrozenGetPartLines(part, cardinal, tolerance),
            PartGeometry.GetPartLines(part, cardinal, tolerance));
        Assert.Equal(values, ProgramBits(program));
        AssertLines(FrozenGetPartLines(part, vector, tolerance),
            PartGeometry.GetPartLines(part, vector, tolerance));
        Assert.Equal(values, ProgramBits(program));
        Assert.Equal(drawingValues, ProgramBits(part.BaseDrawing.Program));
        Assert.Equal(location, new[] { Bits(part.Location.X), Bits(part.Location.Y) });
        Assert.Same(program, part.Program);
        Assert.Same(codes, program.Codes);
        Assert.Equal(identities.Length, codes.Count);
        for (var i = 0; i < identities.Length; i++)
            Assert.Same(identities[i], codes[i]);
    }

    private static long[] ProgramBits(Program program)
    {
        var values = new List<long> { (long)program.Mode, Bits(program.Rotation) };
        foreach (var code in program.Codes)
        {
            values.Add((long)code.Type);
            var motion = Assert.IsAssignableFrom<Motion>(code);
            values.Add(Bits(motion.EndPoint.X));
            values.Add(Bits(motion.EndPoint.Y));
            values.Add(motion.Suppressed ? 1 : 0);
            if (code is LinearMove line)
                values.Add((long)line.Layer);
            if (code is ArcMove arc)
            {
                values.Add((long)arc.Layer);
                values.Add((long)arc.Rotation);
                values.Add(Bits(arc.CenterPoint.X));
                values.Add(Bits(arc.CenterPoint.Y));
            }
        }
        return values.ToArray();
    }

    private static void AssertLines(IReadOnlyList<Line> expected, IReadOnlyList<Line> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            // Vector.Equals is tolerance-based and cannot establish scalar-bit preservation.
            Assert.Equal(Bits(expected[i].StartPoint.X), Bits(actual[i].StartPoint.X));
            Assert.Equal(Bits(expected[i].StartPoint.Y), Bits(actual[i].StartPoint.Y));
            Assert.Equal(Bits(expected[i].EndPoint.X), Bits(actual[i].EndPoint.X));
            Assert.Equal(Bits(expected[i].EndPoint.Y), Bits(actual[i].EndPoint.Y));
        }
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

    private static Part MakePart(Program program) =>
        new(new Drawing("directional preparation", program), new Vector(13.125, -7.375));

    private static Program RectangleProgram(bool reverse)
    {
        var program = new Program();
        program.MoveTo(1, 2);
        if (reverse)
        {
            program.LineTo(1, 5);
            program.LineTo(5, 5);
            program.LineTo(5, 2);
        }
        else
        {
            program.LineTo(5, 2);
            program.LineTo(5, 5);
            program.LineTo(1, 5);
        }
        program.LineTo(1, 2);
        return program;
    }

    private static void AddRectangle(Program program, double x, double y, double width, double height, LayerType layer)
    {
        program.MoveTo(x, y);
        program.Codes.Add(new LinearMove(x + width, y) { Layer = layer });
        program.Codes.Add(new LinearMove(x + width, y + height) { Layer = layer });
        program.Codes.Add(new LinearMove(x, y + height) { Layer = layer });
        program.Codes.Add(new LinearMove(x, y) { Layer = layer });
    }

    private static Program CurvedProgram()
    {
        var program = new Program();
        // A semicircular perimeter joined to three straight edges, a circular hole,
        // and a rectangular hole exercise circles-first ShapeBuilder ordering.
        program.MoveTo(0, 0);
        program.LineTo(8, 0);
        program.Codes.Add(new ArcMove(8, 6, 8, 3));
        program.LineTo(0, 6);
        program.LineTo(0, 0);
        program.MoveTo(3, 3);
        program.Codes.Add(new ArcMove(3, 3, 2, 3, RotationType.CW));
        AddRectangle(program, 5, 2, 1, 2, LayerType.Cut);
        return program;
    }

    // Frozen from PartGeometry at 8664656658d598e55d7bf85c3b392ef6888a593f.
    // These independent preparation bodies and distinct filters intentionally remain
    // duplicated: exact tokens alone did not prove that their overload bindings agree.
    private static List<Line> FrozenGetPartLines(
        Part part, PushDirection facingDirection, double chordTolerance = 0.001)
    {
        var entities = ConvertProgram.ToGeometry(part.Program);
        var shapes = ShapeBuilder.GetShapes(
            entities.Where(e => SpecialLayers.IsMaterial(e.Layer))
        );
        var lines = new List<Line>();
        foreach (var shape in shapes)
        {
            var polygon = shape.ToPolygonWithTolerance(chordTolerance);
            polygon.Offset(part.Location);
            lines.AddRange(FrozenDirectionalLines(polygon, facingDirection));
        }
        return lines;
    }

    private static List<Line> FrozenGetPartLines(
        Part part, Vector facingDirection, double chordTolerance = 0.001)
    {
        var entities = ConvertProgram.ToGeometry(part.Program);
        var shapes = ShapeBuilder.GetShapes(
            entities.Where(e => SpecialLayers.IsMaterial(e.Layer))
        );
        var lines = new List<Line>();
        foreach (var shape in shapes)
        {
            var polygon = shape.ToPolygonWithTolerance(chordTolerance);
            polygon.Offset(part.Location);
            lines.AddRange(FrozenDirectionalLines(polygon, facingDirection));
        }
        return lines;
    }

    private static List<Line> FrozenDirectionalLines(Polygon polygon, Vector direction)
    {
        if (polygon.Vertices.Count < 3)
            return polygon.ToLines();
        var sign = polygon.RotationDirection() == RotationType.CCW ? 1.0 : -1.0;
        var lines = new List<Line>();
        var last = polygon.Vertices[0];
        for (var i = 1; i < polygon.Vertices.Count; i++)
        {
            var current = polygon.Vertices[i];
            var edx = current.X - last.X;
            var edy = current.Y - last.Y;
            var keep = sign * (edy * direction.X - edx * direction.Y) > 0;
            if (keep)
                lines.Add(new Line(last, current));
            last = current;
        }
        return lines;
    }

    private static List<Line> FrozenDirectionalLines(Polygon polygon, PushDirection facingDirection)
    {
        if (polygon.Vertices.Count < 3)
            return polygon.ToLines();
        var sign = polygon.RotationDirection() == RotationType.CCW ? 1.0 : -1.0;
        var lines = new List<Line>();
        var last = polygon.Vertices[0];
        for (int i = 1; i < polygon.Vertices.Count; i++)
        {
            var current = polygon.Vertices[i];
            var dx = current.X - last.X;
            var dy = current.Y - last.Y;
            bool keep;
            switch (facingDirection)
            {
                case PushDirection.Left:
                    keep = -sign * dy > 0;
                    break;
                case PushDirection.Right:
                    keep = sign * dy > 0;
                    break;
                case PushDirection.Up:
                    keep = -sign * dx > 0;
                    break;
                case PushDirection.Down:
                    keep = sign * dx > 0;
                    break;
                default:
                    keep = true;
                    break;
            }
            if (keep)
                lines.Add(new Line(last, current));
            last = current;
        }
        return lines;
    }
}
