using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Tests.Geometry;

public class ContourClassificationTests
{
    private static Shape MakeRectShape(double x, double y, double w, double h)
    {
        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(x, y), new Vector(x + w, y)));
        shape.Entities.Add(new Line(new Vector(x + w, y), new Vector(x + w, y + h)));
        shape.Entities.Add(new Line(new Vector(x + w, y + h), new Vector(x, y + h)));
        shape.Entities.Add(new Line(new Vector(x, y + h), new Vector(x, y)));
        return shape;
    }

    private static Shape MakeCircleShape(double cx, double cy, double r)
    {
        var shape = new Shape();
        shape.Entities.Add(new Circle(new Vector(cx, cy), r));
        return shape;
    }

    private static Shape MakeEtchShape()
    {
        var etchLayer = new Layer("ETCH");
        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(10, 10), new Vector(50, 10)) { Layer = etchLayer });
        return shape;
    }

    [Fact]
    public void Classify_identifies_largest_shape_as_perimeter()
    {
        var shapes = new List<Shape>
        {
            MakeCircleShape(25, 25, 5),
            MakeRectShape(0, 0, 100, 50),
            MakeCircleShape(75, 25, 5),
        };

        var contours = ContourInfo.Classify(shapes);

        Assert.Equal(3, contours.Count);
        Assert.Single(contours, c => c.Type == ContourClassification.Perimeter);
        var perimeter = contours.First(c => c.Type == ContourClassification.Perimeter);
        Assert.Same(shapes[1], perimeter.Shape);
    }

    [Fact]
    public void Classify_identifies_closed_non_perimeter_as_holes()
    {
        var shapes = new List<Shape>
        {
            MakeCircleShape(25, 25, 5),
            MakeRectShape(0, 0, 100, 50),
            MakeCircleShape(75, 25, 5),
        };

        var contours = ContourInfo.Classify(shapes);

        var holes = contours.Where(c => c.Type == ContourClassification.Hole).ToList();
        Assert.Equal(2, holes.Count);
    }

    [Fact]
    public void Classify_identifies_etch_layer_shapes()
    {
        var shapes = new List<Shape>
        {
            MakeRectShape(0, 0, 100, 50),
            MakeEtchShape(),
        };

        var contours = ContourInfo.Classify(shapes);

        Assert.Single(contours, c => c.Type == ContourClassification.Etch);
    }

    [Fact]
    public void Classify_identifies_open_shapes()
    {
        var openShape = new Shape();
        openShape.Entities.Add(new Line(new Vector(0, 0), new Vector(10, 0)));
        openShape.Entities.Add(new Line(new Vector(10, 0), new Vector(10, 5)));
        // Not closed — doesn't return to (0,0)

        var shapes = new List<Shape>
        {
            MakeRectShape(0, 0, 100, 50),
            openShape,
        };

        var contours = ContourInfo.Classify(shapes);

        Assert.Single(contours, c => c.Type == ContourClassification.Open);
    }

    [Fact]
    public void Classify_orders_holes_first_perimeter_last()
    {
        var shapes = new List<Shape>
        {
            MakeRectShape(0, 0, 100, 50),
            MakeCircleShape(25, 25, 5),
        };

        var contours = ContourInfo.Classify(shapes);

        Assert.Equal(ContourClassification.Hole, contours[0].Type);
        Assert.Equal(ContourClassification.Perimeter, contours[^1].Type);
    }

    [Fact]
    public void Classify_labels_holes_sequentially()
    {
        var shapes = new List<Shape>
        {
            MakeRectShape(0, 0, 100, 50),
            MakeCircleShape(25, 25, 5),
            MakeCircleShape(75, 25, 5),
        };

        var contours = ContourInfo.Classify(shapes);

        var holes = contours.Where(c => c.Type == ContourClassification.Hole).ToList();
        Assert.Equal("Hole 1", holes[0].Label);
        Assert.Equal("Hole 2", holes[1].Label);
    }

    [Fact]
    public void Classify_single_shape_is_perimeter()
    {
        var shapes = new List<Shape> { MakeRectShape(0, 0, 50, 30) };

        var contours = ContourInfo.Classify(shapes);

        Assert.Single(contours);
        Assert.Equal(ContourClassification.Perimeter, contours[0].Type);
        Assert.Equal("Perimeter", contours[0].Label);
    }

    [Fact]
    public void Reverse_changes_direction_label()
    {
        var shape = MakeRectShape(0, 0, 100, 50);
        var contours = ContourInfo.Classify(new List<Shape> { shape });
        var contour = contours[0];

        var originalDirection = contour.DirectionLabel;
        contour.Reverse();
        var newDirection = contour.DirectionLabel;

        Assert.NotEqual(originalDirection, newDirection);
    }

    [Fact]
    public void Reverse_changes_direction_label_for_circle()
    {
        var shape = MakeCircleShape(0, 0, 10);
        var contours = ContourInfo.Classify(new List<Shape> { shape });
        var contour = contours[0];

        var originalDirection = contour.DirectionLabel;
        contour.Reverse();
        var newDirection = contour.DirectionLabel;

        Assert.NotEqual(originalDirection, newDirection);
    }
}
