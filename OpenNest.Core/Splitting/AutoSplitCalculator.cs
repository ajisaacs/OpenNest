using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest;

public static class AutoSplitCalculator
{
    public static List<SplitLine> FitToPlate(Box partBounds, double plateWidth, double plateHeight,
        double edgeSpacing, double featureOverhang)
    {
        var usableWidth = plateWidth - 2 * edgeSpacing - featureOverhang;
        var usableHeight = plateHeight - 2 * edgeSpacing - featureOverhang;

        var lines = new List<SplitLine>();

        var verticalSplits = usableWidth > 0 ? (int)System.Math.Ceiling(partBounds.Width / usableWidth) - 1 : 0;
        var horizontalSplits = usableHeight > 0 ? (int)System.Math.Ceiling(partBounds.Length / usableHeight) - 1 : 0;

        if (verticalSplits < 0) verticalSplits = 0;
        if (horizontalSplits < 0) horizontalSplits = 0;

        if (verticalSplits > 0)
        {
            var spacing = partBounds.Width / (verticalSplits + 1);
            for (var i = 1; i <= verticalSplits; i++)
                lines.Add(new SplitLine(partBounds.X + spacing * i, CutOffAxis.Vertical));
        }

        if (horizontalSplits > 0)
        {
            var spacing = partBounds.Length / (horizontalSplits + 1);
            for (var i = 1; i <= horizontalSplits; i++)
                lines.Add(new SplitLine(partBounds.Y + spacing * i, CutOffAxis.Horizontal));
        }

        return lines;
    }

    public static List<SplitLine> SplitByCount(Box partBounds, int horizontalPieces, int verticalPieces)
    {
        var lines = new List<SplitLine>();

        if (verticalPieces > 1)
        {
            var spacing = partBounds.Width / verticalPieces;
            for (var i = 1; i < verticalPieces; i++)
                lines.Add(new SplitLine(partBounds.X + spacing * i, CutOffAxis.Vertical));
        }

        if (horizontalPieces > 1)
        {
            var spacing = partBounds.Length / horizontalPieces;
            for (var i = 1; i < horizontalPieces; i++)
                lines.Add(new SplitLine(partBounds.Y + spacing * i, CutOffAxis.Horizontal));
        }

        return lines;
    }
}
