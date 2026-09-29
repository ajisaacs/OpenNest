using System.Collections.Immutable;

namespace OpenNest.Reporting;

public sealed record ReportPoint(double X, double Y);

public sealed record ReportBounds(double Left, double Bottom, double Right, double Top)
{
    public double Length => Right - Left;
    public double Width => Top - Bottom;
}

/// <summary>Native line or arc; angles are radians and a null center denotes a line.</summary>
public sealed record ReportSegment(ReportPoint Start, ReportPoint End, ReportPoint? Center,
    double Radius, double StartAngle, double SweepAngle);

public sealed record ReportContour(ImmutableArray<ReportSegment> Segments, bool Closed);

public sealed record ReportGeometry(ImmutableArray<ReportContour> Contours, ReportBounds Bounds)
{
    public bool StrokeOnly => Contours.Any(contour => !contour.Closed);
}

public sealed record ReportDrawing(string Id, string Name, long Required, long Nested,
    long Shortage, long Extra, ImmutableArray<int> Plates, ReportGeometry Geometry);

public sealed record ReportPlacement(string ReportId, ReportGeometry Geometry);

public sealed record ReportPlate(int Number, ReportBounds Bounds, int Copies, double PartSpacing,
    double Utilization, ImmutableArray<ReportPlacement> Parts, ImmutableArray<ReportGeometry> Cutoffs);

/// <summary>A detached value snapshot. No domain objects or mutable geometry are retained.</summary>
public sealed record NestReportSnapshot(string Name, string Customer, string Notes, string Material,
    string Grade, double Thickness, string Units, DateTimeOffset GeneratedAt,
    ImmutableArray<ReportDrawing> Drawings, ImmutableArray<ReportPlate> Plates)
{
    public long TotalSheets => Plates.Aggregate(0L, (total, plate) => checked(total + plate.Copies));
}
