using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.IO.Tests;

/// <summary>Drawings the persistence tests share.</summary>
internal static class TestDrawings
{
    /// <summary>A 4 x 4 square drawing named "Square", built from lines.</summary>
    public static Drawing Square() =>
        new("Square", ConvertGeometry.ToProgram(new List<Entity>
        {
            new Line(0, 0, 4, 0),
            new Line(4, 0, 4, 4),
            new Line(4, 4, 0, 4),
            new Line(0, 4, 0, 0),
        }));
}
