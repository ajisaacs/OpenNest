using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace OpenNest.Reporting;

/// <summary>
/// Renders a detached <see cref="NestReportSnapshot"/> to PDF: MigraDoc owns text, tables and
/// pagination; PDFsharp draws vector thumbnails and the sheet diagram into reserved layout areas.
/// </summary>
public static class NestPdfWriter
{
    // Slice 1 layout limits. Anything beyond them fails before the destination is touched.
    private const double Margin = 36;
    private const double BottomMargin = 50;
    private const double ThumbnailWidth = 54;
    private const double ThumbnailHeight = 36;
    private const double DiagramHeight = 320;
    private const double DiagramInset = 5;
    private const double LabelFontSize = 7;
    private const double LabelPadding = 1;

    private static readonly XColor Fill = XColor.FromArgb(222, 222, 222);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // PDFsharp/MigraDoc layout and font state is process-wide and not safe for concurrent
    // documents: parallel exports laid out text differently. Reports are rare, so serialize them.
    private static readonly object RenderLock = new();

    /// <summary>Thread-safe; concurrent calls are serialized.</summary>
    public static void Write(NestReportSnapshot snapshot, string destination) =>
        Write(snapshot, destination, null);

    /// <param name="wrapOutput">Test seam for injecting late write failures.</param>
    internal static void Write(NestReportSnapshot snapshot, string destination, Func<Stream, Stream>? wrapOutput)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        lock (RenderLock)
            WriteSerialized(snapshot, destination, wrapOutput);
    }

    private static void WriteSerialized(NestReportSnapshot snapshot, string destination, Func<Stream, Stream>? wrapOutput)
    {
        ValidateText(snapshot);
        if (snapshot.Plates.Length > 1)
            throw new NotSupportedException($"A report with {snapshot.Plates.Length} plate layouts is not supported by this report slice; it supports at most one layout.");

        ReportFonts.Initialize();
        var document = new Document();
        document.Info.Title = snapshot.Name;
        var normal = document.Styles[StyleNames.Normal]
            ?? throw new InvalidOperationException("MigraDoc normal style is unavailable.");
        normal.Font.Name = ReportFonts.Family;
        normal.Font.Size = 9;
        var diagrams = new List<Table>();
        AddSummary(document, snapshot);
        foreach (var plate in snapshot.Plates)
            diagrams.Add(AddPlate(document, snapshot, plate));

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        // PDFsharp 6.2.4 disallows reading the page count after Save.
        var pageCount = pdf.PageCount;
        var areas = LocateDiagrams(renderer, pageCount, diagrams);
        for (var index = 0; index < areas.Count; index++)
        {
            var (page, area) = areas[index];
            if (page != index + 2)
                throw new NotSupportedException("A summary longer than one page is not supported by this report slice.");
            using var gfx = XGraphics.FromPdfPage(pdf.Pages[page - 1]);
            DrawPlate(gfx, snapshot, snapshot.Plates[index], area);
        }
        if (pageCount != 1 + snapshot.Plates.Length)
            throw new NotSupportedException(snapshot.Plates.Length == 0
                ? "A summary longer than one page is not supported by this report slice."
                : "A plate page longer than one page is not supported by this report slice.");

        pdf.Info.Title = snapshot.Name;
        pdf.Info.Creator = "OpenNest";
        pdf.Info.CreationDate = snapshot.GeneratedAt.UtcDateTime;
        AtomicReportFile.Write(destination, stream => pdf.Save(wrapOutput?.Invoke(stream) ?? stream, false));
    }

    private static void ValidateText(NestReportSnapshot snapshot)
    {
        ReportFonts.ValidateText(snapshot.Name, "Nest name");
        ReportFonts.ValidateText(snapshot.Customer, "Customer");
        ReportFonts.ValidateText(snapshot.Notes, "Notes");
        ReportFonts.ValidateText(snapshot.Material, "Material");
        ReportFonts.ValidateText(snapshot.Grade, "Material grade");
        foreach (var drawing in snapshot.Drawings)
            ReportFonts.ValidateText(drawing.Name, $"Drawing {drawing.Id} name");
    }

    private static void AddSummary(Document document, NestReportSnapshot snapshot)
    {
        var section = AddSection(document, Orientation.Portrait);
        var title = section.AddParagraph("Nest Report");
        title.Format.Font.Size = 14;
        title.Format.Font.Bold = true;
        title.Format.SpaceAfter = Unit.FromPoint(6);

        var info = AddTable(section, 130, 410);
        info.Borders.Visible = false;
        AddInfoRow(info, "Nest:", snapshot.Name);
        if (snapshot.Customer.Length > 0)
            AddInfoRow(info, "Customer:", snapshot.Customer);
        if (snapshot.Notes.Length > 0)
            AddInfoRow(info, "Notes:", snapshot.Notes);
        AddInfoRow(info, "Material:", Material(snapshot));
        AddInfoRow(info, "Thickness:", Thickness(snapshot));
        AddInfoRow(info, "Units:", snapshot.Units == "mm" ? "millimeters (mm)" : "inches (in)");
        AddInfoRow(info, "Generated:", snapshot.GeneratedAt.ToString("yyyy-MM-dd HH:mm zzz", Invariant));
        AddInfoRow(info, "Distinct layouts:", snapshot.Plates.Length.ToString(Invariant));
        AddInfoRow(info, "Total physical sheets:", snapshot.TotalSheets.ToString(Invariant));

        AddHeading(section, "Plates");
        if (snapshot.Plates.Length == 0)
            section.AddParagraph("No plates in this job.");
        else
        {
            var plates = AddTable(section, 50, 150, 70, 80, 80);
            AddHeader(plates, "Plate", "Stock size", "Copies", "Parts/sheet", "Utilization");
            foreach (var plate in snapshot.Plates)
                AddRow(plates, plate.Number.ToString(Invariant), SheetSize(snapshot, plate),
                    plate.Copies.ToString(Invariant), plate.Parts.Length.ToString(Invariant), Percent(plate.Utilization));
        }

        AddHeading(section, "Parts");
        if (snapshot.Drawings.Length == 0)
        {
            section.AddParagraph("No parts in this job.");
            return;
        }
        var parts = AddTable(section, 36, 62, 170, 50, 50, 52, 40, 80);
        AddHeader(parts, "ID", "Part", "Drawing", "Required", "Nested", "Shortage", "Extra", "Plates");
        foreach (var drawing in snapshot.Drawings)
        {
            var row = AddRow(parts, drawing.Id, null, drawing.Name, drawing.Required.ToString(Invariant),
                drawing.Nested.ToString(Invariant), drawing.Shortage.ToString(Invariant),
                drawing.Extra.ToString(Invariant), drawing.Plates.Length == 0 ? "-" : string.Join(", ", drawing.Plates));
            var image = row.Cells[1].AddParagraph().AddImage(Thumbnail(drawing.Geometry));
            image.Width = Unit.FromPoint(ThumbnailWidth);
            image.Height = Unit.FromPoint(ThumbnailHeight);
        }
    }

    private static Table AddPlate(Document document, NestReportSnapshot snapshot, ReportPlate plate)
    {
        var section = AddSection(document, Orientation.Landscape);
        var title = section.AddParagraph($"Plate {plate.Number} of {snapshot.Plates.Length}");
        title.Format.Font.Size = 13;
        title.Format.Font.Bold = true;

        var info = AddTable(section, 80, 280, 90, 270);
        info.Borders.Visible = false;
        AddInfoRow(info, "Nest:", snapshot.Name, "Sheet size:", SheetSize(snapshot, plate));
        AddInfoRow(info, "Material:", Material(snapshot), "Copies:", plate.Copies.ToString(Invariant));
        AddInfoRow(info, "Thickness:", Thickness(snapshot), "Part spacing:",
            $"{Number(plate.PartSpacing)} {snapshot.Units}");
        AddInfoRow(info, "Parts/sheet:", plate.Parts.Length.ToString(Invariant), "Utilization:",
            Percent(plate.Utilization) + " (net part area / full sheet)");

        // An empty fixed-height row reserves the vector diagram area in MigraDoc's flow.
        var diagram = AddTable(section, 720);
        diagram.Borders.Visible = false;
        diagram.TopPadding = diagram.BottomPadding = Unit.Zero;
        var reserved = diagram.AddRow();
        reserved.HeightRule = RowHeightRule.Exactly;
        reserved.Height = Unit.FromPoint(DiagramHeight);
        diagram.Format.SpaceBefore = Unit.FromPoint(4);

        var legend = section.AddParagraph("Filled outlines are closed parts; open material paths such as tab gaps are shown without fill. Dashed lines are scrap cutoffs.");
        legend.Format.Font.Size = 7.5;
        legend.Format.SpaceBefore = Unit.FromPoint(4);
        var approval = section.AddParagraph("This report is not a geometry or CNC approval.");
        approval.Format.Font.Size = 7.5;
        approval.Format.SpaceAfter = Unit.FromPoint(4);

        var names = snapshot.Drawings.ToDictionary(drawing => drawing.Id, drawing => drawing.Name, StringComparer.Ordinal);
        var perSheet = plate.Parts.GroupBy(part => part.ReportId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (long)group.Count(), StringComparer.Ordinal);
        var parts = AddTable(section, 50, 330, 100, 140);
        AddHeader(parts, "ID", "Drawing", "Qty/sheet", $"Total ({plate.Copies} copies)");
        foreach (var drawing in snapshot.Drawings.Where(drawing => perSheet.ContainsKey(drawing.Id)))
        {
            var count = perSheet[drawing.Id];
            AddRow(parts, drawing.Id, names[drawing.Id], count.ToString(Invariant),
                checked(count * plate.Copies).ToString(Invariant));
        }
        return diagram;
    }

    private static List<(int Page, XRect Area)> LocateDiagrams(PdfDocumentRenderer renderer, int pageCount, List<Table> diagrams)
    {
        var found = new (int Page, XRect Area)?[diagrams.Count];
        for (var page = 1; page <= pageCount; page++)
        {
            var renderInfos = renderer.DocumentRenderer?.GetRenderInfoFromPage(page)
                ?? throw new InvalidOperationException("MigraDoc render information is unavailable.");
            foreach (var info in renderInfos)
            {
                var index = diagrams.FindIndex(table => ReferenceEquals(table, info.DocumentObject));
                if (index < 0)
                    continue;
                if (found[index] != null)
                    throw new NotSupportedException($"A diagram for plate {index + 1} split across pages is not supported by this report slice.");
                var area = info.LayoutInfo.ContentArea;
                found[index] = (page, new XRect(area.X.Point, area.Y.Point, area.Width.Point, area.Height.Point));
            }
        }
        return found.Select((item, index) => item
            ?? throw new NotSupportedException($"Plate {index + 1}: a missing diagram area is not supported by this report slice.")).ToList();
    }

    private static void DrawPlate(XGraphics gfx, NestReportSnapshot snapshot, ReportPlate plate, XRect area)
    {
        var inner = new XRect(area.X + DiagramInset, area.Y + DiagramInset,
            area.Width - 2 * DiagramInset, DiagramHeight - 2 * DiagramInset);
        var fit = Fit.Create(plate.Bounds, inner);
        var font = new XFont(ReportFonts.Family, LabelFontSize, XFontStyleEx.Bold);

        // Validate every label before drawing: dense-label callouts are a later slice.
        var labels = new List<(string Id, XRect Box)>();
        for (var index = 0; index < plate.Parts.Length; index++)
        {
            var part = plate.Parts[index];
            var bounds = fit.Rect(part.Geometry.Bounds);
            var size = gfx.MeasureString(part.ReportId, font);
            var width = size.Width + 2 * LabelPadding;
            var height = size.Height + 2 * LabelPadding;
            if (width > bounds.Width || height > bounds.Height)
                throw new NotSupportedException($"Plate {plate.Number}, part {index + 1} ({part.ReportId}): a label that does not fit inside its part at {LabelFontSize} pt is not supported by this report slice.");
            var box = new XRect(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
            foreach (var other in labels)
            {
                if (box.IntersectsWith(other.Box))
                    throw new NotSupportedException($"Plate {plate.Number}: overlapping labels {other.Id} and {part.ReportId} are not supported by this report slice.");
            }
            labels.Add((part.ReportId, box));
        }

        gfx.DrawRectangle(new XPen(XColors.Black, 1), fit.Rect(plate.Bounds));
        var outline = new XPen(XColors.Black, 0.6);
        foreach (var part in plate.Parts)
            DrawGeometry(gfx, part.Geometry, fit, outline);
        var cutoff = new XPen(XColors.Black, 0.75) { DashStyle = XDashStyle.Dash };
        foreach (var geometry in plate.Cutoffs)
            StrokeContours(gfx, geometry, fit, cutoff);
        foreach (var (id, box) in labels)
        {
            gfx.DrawRectangle(XBrushes.White, box);
            gfx.DrawString(id, font, XBrushes.Black, box, XStringFormats.Center);
        }
    }

    private static string Thumbnail(ReportGeometry geometry)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(ThumbnailWidth);
        page.Height = XUnit.FromPoint(ThumbnailHeight);
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var fit = Fit.Create(geometry.Bounds, new XRect(2, 2, ThumbnailWidth - 4, ThumbnailHeight - 4));
            DrawGeometry(gfx, geometry, fit, new XPen(XColors.Black, 0.5));
        }
        using var stream = new MemoryStream();
        document.Save(stream, false);
        // MigraDoc embeds a PDF image source as a vector form XObject, not a raster image.
        return "base64:" + Convert.ToBase64String(stream.ToArray());
    }

    private static void DrawGeometry(XGraphics gfx, ReportGeometry geometry, Fit fit, XPen pen)
    {
        if (geometry.StrokeOnly)
        {
            StrokeContours(gfx, geometry, fit, pen);
            return;
        }
        // Every contour is closed here, so CloseFigure separates the subpaths.
        var path = new XGraphicsPath();
        foreach (var contour in geometry.Contours)
        {
            AddContour(path, contour, fit);
            path.CloseFigure();
        }
        path.FillMode = XFillMode.Alternate; // Even-odd: holes stay unfilled.
        gfx.DrawPath(pen, new XSolidBrush(Fill), path);
    }

    /// <summary>
    /// PDFsharp's StartFigure does not emit a move-to after an open figure, which would draw a
    /// false segment across a tab gap to the next contour. Stroke each contour as its own path.
    /// </summary>
    private static void StrokeContours(XGraphics gfx, ReportGeometry geometry, Fit fit, XPen pen)
    {
        foreach (var contour in geometry.Contours)
        {
            var path = new XGraphicsPath();
            AddContour(path, contour, fit);
            if (contour.Closed)
                path.CloseFigure();
            gfx.DrawPath(pen, path);
        }
    }

    private static void AddContour(XGraphicsPath path, ReportContour contour, Fit fit)
    {
        foreach (var segment in contour.Segments)
        {
            if (segment.Center == null)
            {
                path.AddLine(fit.Point(segment.Start), fit.Point(segment.End));
                continue;
            }
            var center = fit.Point(segment.Center);
            var radius = segment.Radius * fit.Scale;
            var box = new XRect(center.X - radius, center.Y - radius, 2 * radius, 2 * radius);
            // Page Y points down, so model angles and sweeps change sign.
            path.AddArc(box, -Degrees(segment.StartAngle), -Degrees(segment.SweepAngle));
        }
    }

    private static double Degrees(double radians) => radians * 180 / System.Math.PI;

    private static Section AddSection(Document document, Orientation orientation)
    {
        var section = document.AddSection();
        var setup = section.PageSetup;
        setup.PageFormat = PageFormat.Letter;
        setup.Orientation = orientation;
        setup.TopMargin = setup.LeftMargin = setup.RightMargin = Unit.FromPoint(Margin);
        setup.BottomMargin = Unit.FromPoint(BottomMargin);
        setup.FooterDistance = Unit.FromPoint(20);
        var footer = section.Footers.Primary.AddParagraph("Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();
        footer.AddText("  |  Diagram fitted to page; not a dimensioned cutting drawing.");
        footer.Format.Font.Size = 7.5;
        return section;
    }

    private static void AddHeading(Section section, string text)
    {
        var heading = section.AddParagraph(text);
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 11;
        heading.Format.SpaceBefore = Unit.FromPoint(10);
        heading.Format.SpaceAfter = Unit.FromPoint(4);
    }

    private static Table AddTable(Section section, params double[] widths)
    {
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = Colors.Gray;
        table.LeftPadding = table.RightPadding = Unit.FromPoint(3);
        table.TopPadding = table.BottomPadding = Unit.FromPoint(1.5);
        foreach (var width in widths)
            table.AddColumn(Unit.FromPoint(width));
        return table;
    }

    private static void AddHeader(Table table, params string[] cells)
    {
        var row = AddRow(table, cells);
        row.HeadingFormat = true;
        row.Format.Font.Bold = true;
        row.Shading.Color = Colors.LightGray;
    }

    private static void AddInfoRow(Table table, params string[] cells)
    {
        var row = AddRow(table, cells);
        for (var index = 0; index < cells.Length; index += 2)
            row.Cells[index].Format.Font.Bold = true;
    }

    /// <summary>Adds text cells; a null cell is left empty for non-text content.</summary>
    private static Row AddRow(Table table, params string?[] cells)
    {
        var row = table.AddRow();
        row.VerticalAlignment = VerticalAlignment.Center;
        for (var index = 0; index < cells.Length; index++)
        {
            if (cells[index] == null)
                continue;
            var paragraph = row.Cells[index].AddParagraph();
            var lines = cells[index]!.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ').Split('\n');
            for (var line = 0; line < lines.Length; line++)
            {
                if (line > 0)
                    paragraph.AddLineBreak();
                paragraph.AddText(lines[line]);
            }
        }
        return row;
    }

    private static string Material(NestReportSnapshot snapshot)
    {
        var material = $"{snapshot.Material} {snapshot.Grade}".Trim();
        return material.Length == 0 ? "Not specified" : material;
    }

    // Zero thickness means "unset" in a new nest; never present it as a genuine value.
    private static string Thickness(NestReportSnapshot snapshot) =>
        snapshot.Thickness > 0 ? $"{Number(snapshot.Thickness)} {snapshot.Units}" : "Not specified";

    private static string SheetSize(NestReportSnapshot snapshot, ReportPlate plate) =>
        $"{Number(plate.Bounds.Length)} x {Number(plate.Bounds.Width)} {snapshot.Units}";

    private static string Number(double value) => value.ToString("0.####", Invariant);

    private static string Percent(double value) => (value * 100).ToString("0.0", Invariant) + "%";

    /// <summary>One aspect-preserving model-to-page transform, centered, with the Y axis flipped.</summary>
    private readonly record struct Fit(ReportBounds Bounds, double Scale, double X, double Y)
    {
        public static Fit Create(ReportBounds bounds, XRect area)
        {
            var scales = new List<double>(2);
            if (bounds.Length > 0)
                scales.Add(area.Width / bounds.Length);
            if (bounds.Width > 0)
                scales.Add(area.Height / bounds.Width);
            var scale = scales.Count == 0 ? 1 : scales.Min();
            if (!double.IsFinite(scale) || scale <= 0)
                throw new InvalidOperationException("Report geometry cannot be fitted to the page.");
            return new Fit(bounds, scale,
                area.X + (area.Width - bounds.Length * scale) / 2,
                area.Y + (area.Height - bounds.Width * scale) / 2);
        }

        public XPoint Point(ReportPoint point) =>
            new(X + (point.X - Bounds.Left) * Scale, Y + (Bounds.Top - point.Y) * Scale);

        public XRect Rect(ReportBounds box)
        {
            var low = Point(new ReportPoint(box.Left, box.Top));
            return new XRect(low.X, low.Y, box.Length * Scale, box.Width * Scale);
        }
    }
}
