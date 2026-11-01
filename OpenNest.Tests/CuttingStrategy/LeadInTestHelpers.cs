using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingStrategy;

/// <summary>Helpers the lead-in tests share. Import with <c>using static</c>.</summary>
internal static class LeadInTestHelpers
{
    /// <summary>Asserts both coordinates match to 8 decimal places.</summary>
    public static void AssertPoint(Vector expected, Vector actual)
    {
        Assert.Equal(expected.X, actual.X, 8);
        Assert.Equal(expected.Y, actual.Y, 8);
    }

    /// <summary>The part's shape profile, leaving out rapid, scribe and break layers.</summary>
    public static ShapeProfile Profile(Part part) => new(part.Program.ToGeometry()
        .Where(e => SpecialLayers.IsMaterial(e.Layer)).ToList());
}
