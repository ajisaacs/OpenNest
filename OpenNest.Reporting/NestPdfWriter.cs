using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace OpenNest.Reporting;

/// <summary>
/// Renders a detached <see cref="NestReportSnapshot"/> to PDF: MigraDoc owns text, tables and
/// pagination; PDFsharp draws vector thumbnails and sheet diagrams into reserved layout areas.
/// </summary>
public static class NestPdfWriter
{
    private const double Margin = 36;
    private const double BottomMargin = 50;
    private const double HeaderDistance = 18;
    private const double HeaderFontSize = 7.5;
    private const int MaxHeaderLines = 3;
    private const double BodyFontSize = 9;
    private const double CellPadding = 3;
    private const double ThumbnailWidth = 54;
    private const double ThumbnailHeight = 36;
    private const double PortraitWidth = 540;
    private const double LandscapeWidth = 720;
    private const double OverviewHeight = 320;
    private const double DetailHeight = 400;

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
        ReportFonts.Initialize();

        // Plan every diagram before layout; nothing below touches the destination until Save.
        var plans = snapshot.Plates.Select(plate =>
            NestReportDiagram.Plan(plate, LandscapeWidth, OverviewHeight, DetailHeight)).ToList();

        var document = new Document();
        document.Info.Title = snapshot.Name;
        var normal = document.Styles[StyleNames.Normal]
            ?? throw new InvalidOperationException("MigraDoc normal style is unavailable.");
        normal.Font.Name = ReportFonts.Family;
        normal.Font.Size = BodyFontSize;
        var areas = new List<(Table Table, Action<XGraphics, XRect> Draw)>();
        AddSummary(document, snapshot);
        for (var index = 0; index < snapshot.Plates.Length; index++)
            AddPlate(document, snapshot, snapshot.Plates[index], plans[index], areas);

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        // PDFsharp 6.2.4 disallows reading the page count after Save.
        var pageCount = pdf.PageCount;
        foreach (var (page, area, draw) in LocateAreas(renderer, pageCount, areas))
        {
            using var gfx = XGraphics.FromPdfPage(pdf.Pages[page - 1]);
            var state = gfx.Save();
            gfx.TranslateTransform(area.X, area.Y);
            gfx.IntersectClip(new XRect(0, 0, area.Width, area.Height));
            draw(gfx, area);
            gfx.Restore(state);
        }

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
        var section = AddSection(document, Orientation.Portrait, PortraitWidth, $"Nest report: {snapshot.Name}");
        var title = section.AddParagraph("Nest Report");
        title.Format.Font.Size = 14;
        title.Format.Font.Bold = true;
        title.Format.SpaceAfter = Unit.FromPoint(6);

        var info = AddTable(section, 130, 410);
        info.Borders.Visible = false;
        AddInfoRow(info, "Nest:", snapshot.Name);
        if (snapshot.Customer.Length > 0)
            AddInfoRow(info, "Customer:", snapshot.Customer);
        AddInfoRow(info, "Material:", Material(snapshot));
        AddInfoRow(info, "Thickness:", Thickness(snapshot));
        AddInfoRow(info, "Units:", snapshot.Units == "mm" ? "millimeters (mm)" : "inches (in)");
        AddInfoRow(info, "Generated:", snapshot.GeneratedAt.ToString("yyyy-MM-dd HH:mm zzz", Invariant));
        AddInfoRow(info, "Distinct layouts:", snapshot.Plates.Length.ToString(Invariant));
        AddInfoRow(info, "Total physical sheets:", snapshot.TotalSheets.ToString(Invariant));
        if (snapshot.Notes.Length > 0)
        {
            // Free paragraphs split across pages; a table row taller than a page would be clipped.
            AddHeading(section, "Notes");
            AddLines(section.AddParagraph(), ReportText.Wrap(snapshot.Notes, BodyFont(false), PortraitWidth));
        }

        AddHeading(section, "Plates");
        if (snapshot.Plates.Length == 0)
            section.AddParagraph("No plates in this job.");
        else
        {
            var plates = AddTable(section, 60, 200, 80, 100, 100);
            AddHeader(plates, "Plate", "Stock size", "Copies", "Parts/sheet", "Utilization");
            foreach (var plate in snapshot.Plates)
                AddRow(plates, $"Plate {plate.Number}", false, plate.Number.ToString(Invariant), SheetSize(snapshot, plate),
                    plate.Copies.ToString(Invariant), plate.Parts.Length.ToString(Invariant), Percent(plate.Utilization));
        }

        AddHeading(section, "Parts");
        if (snapshot.Drawings.Length == 0)
        {
            section.AddParagraph("No parts in this job.");
            return;
        }
        // Column widths fit the bold headings on one line (measured, plus padding).
        var parts = AddTable(section, 36, 62, 138, 56, 50, 56, 42, 100);
        AddHeader(parts, "ID", "Part", "Drawing", "Required", "Nested", "Shortage", "Extra", "Plates");
        foreach (var drawing in snapshot.Drawings)
        {
            var row = AddRow(parts, $"Drawing {drawing.Id}", false, drawing.Id, null, drawing.Name,
                drawing.Required.ToString(Invariant), drawing.Nested.ToString(Invariant),
                drawing.Shortage.ToString(Invariant), drawing.Extra.ToString(Invariant), ReportText.Ranges(drawing.Plates));
            var image = row.Cells[1].AddParagraph().AddImage(Thumbnail(drawing.Geometry));
            image.Width = Unit.FromPoint(ThumbnailWidth);
            image.Height = Unit.FromPoint(ThumbnailHeight);
        }
    }

    private static void AddPlate(Document document, NestReportSnapshot snapshot, ReportPlate plate,
        PlateDiagramPlan plan, List<(Table, Action<XGraphics, XRect>)> areas)
    {
        var heading = $"Plate {plate.Number} of {snapshot.Plates.Length}";
        var header = new List<string> { $"Nest report: {snapshot.Name}", heading, SheetSize(snapshot, plate) };
        // Unset material/thickness are stated in the page body, not repeated in every header.
        if (snapshot.Material.Length + snapshot.Grade.Length > 0)
            header.Add(Material(snapshot));
        if (snapshot.Thickness > 0)
            header.Add(Thickness(snapshot));
        var section = AddSection(document, Orientation.Landscape, LandscapeWidth, string.Join("  |  ", header));
        var title = section.AddParagraph(heading);
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

        var overview = AddReservedArea(section, null, plan.Overview.Size.Height);
        areas.Add((overview, (gfx, _) => DrawView(gfx, plate, plan.Overview)));

        var legend = section.AddParagraph("Filled outlines are closed parts; open material paths such as tab gaps are shown without fill. Dashed lines are scrap cutoffs.");
        legend.Format.Font.Size = 7.5;
        legend.Format.SpaceBefore = Unit.FromPoint(4);
        if (plan.Overview.Grid is { } grid)
        {
            var names = string.Join(", ", plan.Details.Select(detail => detail.Cell));
            var fallback = section.AddParagraph();
            fallback.Format.Font.Size = 7.5;
            AddLines(fallback, ReportText.Wrap(
                $"Some part IDs are too small to label at this scale. Grid rows are lettered from the top and columns numbered from the left; cells outlined dash-dot have detail views on the following pages: {names}.",
                new XFont(ReportFonts.Family, 7.5), LandscapeWidth));
        }
        var approval = section.AddParagraph("This report is not a geometry or CNC approval.");
        approval.Format.Font.Size = 7.5;
        approval.Format.SpaceAfter = Unit.FromPoint(4);

        var names2 = snapshot.Drawings.ToDictionary(drawing => drawing.Id, drawing => drawing.Name, StringComparer.Ordinal);
        var perSheet = plate.Parts.GroupBy(part => part.ReportId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (long)group.Count(), StringComparer.Ordinal);
        var parts = AddTable(section, 60, 400, 110, 150);
        AddHeader(parts, "ID", "Drawing", "Qty/sheet", $"Total ({plate.Copies} copies)");
        foreach (var drawing in snapshot.Drawings.Where(drawing => perSheet.ContainsKey(drawing.Id)))
        {
            var count = perSheet[drawing.Id];
            AddRow(parts, $"Plate {plate.Number}, drawing {drawing.Id}", false, drawing.Id, names2[drawing.Id],
                count.ToString(Invariant), checked(count * plate.Copies).ToString(Invariant));
        }

        foreach (var detail in plan.Details)
        {
            var bounds = detail.CellBounds!;
            var caption = $"Plate {plate.Number} detail {detail.Cell}: X {Number(bounds.Left)} to {Number(bounds.Right)}, Y {Number(bounds.Bottom)} to {Number(bounds.Top)} {snapshot.Units}. Parts centered in the dash-dot cell are labeled.";
            var table = AddReservedArea(section, caption, detail.Size.Height);
            areas.Add((table, (gfx, _) => DrawView(gfx, plate, detail)));
        }
    }

    /// <summary>A fixed-height, never-split table row reserves a vector drawing area in MigraDoc's flow.</summary>
    private static Table AddReservedArea(Section section, string? caption, double height)
    {
        var table = AddTable(section, LandscapeWidth);
        table.Borders.Visible = false;
        table.LeftPadding = table.RightPadding = Unit.Zero;
        table.TopPadding = table.BottomPadding = Unit.Zero;
        table.Format.SpaceBefore = Unit.FromPoint(4);
        if (caption != null)
        {
            var row = AddRow(table, "Detail caption", true, caption);
            row.KeepWith = 1;
        }
        var reserved = table.AddRow();
        reserved.HeightRule = RowHeightRule.Exactly;
        reserved.Height = Unit.FromPoint(height);
        return table;
    }

    private static IEnumerable<(int Page, XRect Area, Action<XGraphics, XRect> Draw)> LocateAreas(
        PdfDocumentRenderer renderer, int pageCount, List<(Table Table, Action<XGraphics, XRect> Draw)> areas)
    {
        var found = new (int Page, XRect Area)?[areas.Count];
        for (var page = 1; page <= pageCount; page++)
        {
            var renderInfos = renderer.DocumentRenderer?.GetRenderInfoFromPage(page)
                ?? throw new InvalidOperationException("MigraDoc render information is unavailable.");
            foreach (var info in renderInfos)
            {
                var index = areas.FindIndex(area => ReferenceEquals(area.Table, info.DocumentObject));
                if (index < 0)
                    continue;
                var table = areas[index].Table;
                // Only the page holding the reserved (last) row receives the drawing.
                if (info.FormatInfo is not TableFormatInfo format || format.EndRow != table.Rows.Count - 1)
                    continue;
                if (found[index] != null)
                    throw new InvalidOperationException($"Report drawing area {index + 1} was laid out twice.");
                var content = info.LayoutInfo.ContentArea;
                var height = table.Rows[table.Rows.Count - 1].Height.Point;
                found[index] = (page, new XRect(content.X.Point, content.Y.Point + content.Height.Point - height,
                    content.Width.Point, height));
            }
        }
        for (var index = 0; index < areas.Count; index++)
        {
            var (page, area) = found[index]
                ?? throw new InvalidOperationException($"Report drawing area {index + 1} was not laid out.");
            yield return (page, area, areas[index].Draw);
        }
    }

    private static void DrawView(XGraphics gfx, ReportPlate plate, DiagramView view)
    {
        var fit = view.Fit;
        // Detail views clip exactly at their frame line so cropped neighbours stop there.
        var frame = new XRect(0.5, 0.5, view.Size.Width - 1, view.Size.Height - 1);
        var state = gfx.Save();
        if (view.CellBounds != null)
            gfx.IntersectClip(frame);
        // The map grid lies beneath the sheet and parts so it never hides an outline.
        if (view.Grid is { } grid)
            DrawGrid(gfx, grid, fit);
        gfx.DrawRectangle(new XPen(XColors.Black, 1), fit.Rect(plate.Bounds));
        var outline = new XPen(XColors.Black, 0.6);
        foreach (var part in plate.Parts)
            DrawGeometry(gfx, part.Geometry, fit, outline);
        // PDFsharp multiplies dash patterns by the pen width; the preset styles are so short at
        // these thin widths that they print as solid lines.
        var cutoff = new XPen(XColors.Black, 0.75) { DashPattern = [9, 3] };
        foreach (var geometry in plate.Cutoffs)
            StrokeContours(gfx, geometry, fit, cutoff);
        // Long dash-dot, so detail cells are never confused with dashed scrap cutoffs.
        var detail = new XPen(XColors.Black, 1.6) { DashPattern = [9, 3, 1, 3] };
        if (view.Grid is { } overviewGrid)
        {
            foreach (var (row, column) in overviewGrid.DetailCells)
                gfx.DrawRectangle(detail, fit.Rect(overviewGrid.Cell(row, column)));
        }
        if (view.CellBounds is { } cell)
            gfx.DrawRectangle(detail, fit.Rect(cell));
        var font = NestReportDiagram.LabelFont();
        foreach (var label in view.Labels)
        {
            gfx.DrawRectangle(XBrushes.White, label.Box);
            gfx.DrawString(label.Id, font, XBrushes.Black, label.Box, XStringFormats.Center);
        }
        gfx.Restore(state);
        // Frame the viewport so cropped neighbouring parts read as intentional.
        if (view.CellBounds != null)
            gfx.DrawRectangle(new XPen(XColors.Gray, 0.75), frame);
    }

    private static void DrawGrid(XGraphics gfx, DiagramGrid grid, Fit fit)
    {
        var line = new XPen(XColors.Gray, 0.3) { DashPattern = [3, 6] };
        var area = fit.Rect(grid.Union);
        for (var column = 0; column <= grid.Columns; column++)
        {
            var x = area.X + column * grid.CellLength * fit.Scale;
            gfx.DrawLine(line, x, area.Y, x, area.Y + grid.Rows * grid.CellWidth * fit.Scale);
        }
        for (var row = 0; row <= grid.Rows; row++)
        {
            var y = area.Y + row * grid.CellWidth * fit.Scale;
            gfx.DrawLine(line, area.X, y, area.X + grid.Columns * grid.CellLength * fit.Scale, y);
        }
        var font = NestReportDiagram.LabelFont();
        for (var column = 0; column < grid.Columns; column++)
        {
            var x = area.X + (column + 0.5) * grid.CellLength * fit.Scale;
            gfx.DrawString((column + 1).ToString(Invariant), font, XBrushes.Black,
                new XRect(x - 20, area.Y - 11, 40, 10), XStringFormats.BottomCenter);
        }
        for (var row = 0; row < grid.Rows; row++)
        {
            var y = area.Y + (row + 0.5) * grid.CellWidth * fit.Scale;
            gfx.DrawString(ReportText.RowName(row), font, XBrushes.Black,
                new XRect(area.X - 16, y - 5, 14, 10), XStringFormats.CenterRight);
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

    private static XFont BodyFont(bool bold) =>
        new(ReportFonts.Family, BodyFontSize, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);

    private static Section AddSection(Document document, Orientation orientation, double width, string header)
    {
        var section = document.AddSection();
        var setup = section.PageSetup;
        setup.PageFormat = PageFormat.Letter;
        setup.Orientation = orientation;
        setup.LeftMargin = setup.RightMargin = Unit.FromPoint(Margin);
        setup.BottomMargin = Unit.FromPoint(BottomMargin);
        setup.FooterDistance = Unit.FromPoint(20);

        // Every page, including continuations, identifies its job and plate. MigraDoc does not
        // push the body below a tall header, so size the top margin from the wrapped header.
        var headerFont = new XFont(ReportFonts.Family, HeaderFontSize);
        var lines = ReportText.Wrap(header, headerFont, width);
        if (lines.Count > MaxHeaderLines)
            throw new NotSupportedException($"Page header (nest name): text needs {lines.Count} lines; this report supports at most {MaxHeaderLines}.");
        setup.HeaderDistance = Unit.FromPoint(HeaderDistance);
        setup.TopMargin = Unit.FromPoint(HeaderDistance + lines.Count * headerFont.GetHeight() + 8);
        var top = section.Headers.Primary.AddParagraph();
        top.Format.Font.Size = HeaderFontSize;
        top.Format.Font.Color = Colors.DimGray;
        AddLines(top, lines);

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
        heading.Format.KeepWithNext = true;
    }

    private static Table AddTable(Section section, params double[] widths)
    {
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = Colors.Gray;
        table.LeftPadding = table.RightPadding = Unit.FromPoint(CellPadding);
        table.TopPadding = table.BottomPadding = Unit.FromPoint(1.5);
        foreach (var width in widths)
            table.AddColumn(Unit.FromPoint(width));
        return table;
    }

    /// <summary>Heading rows repeat at the top of every continuation page.</summary>
    private static void AddHeader(Table table, params string[] cells)
    {
        var row = AddRow(table, "Table heading", true, cells);
        row.HeadingFormat = true;
        row.Format.Font.Bold = true;
        row.Shading.Color = Colors.LightGray;
    }

    private static void AddInfoRow(Table table, params string[] cells)
    {
        var row = table.AddRow();
        row.VerticalAlignment = VerticalAlignment.Top;
        for (var index = 0; index < cells.Length; index++)
        {
            var bold = index % 2 == 0;
            FillCell(table, row, index, cells[index], bold, $"{cells[index - index % 2].TrimEnd(':')} field");
            if (bold)
                row.Cells[index].Format.Font.Bold = true;
        }
    }

    /// <summary>Adds wrapped text cells; a null cell is left empty for non-text content.</summary>
    private static Row AddRow(Table table, string context, bool bold, params string?[] cells)
    {
        var row = table.AddRow();
        row.VerticalAlignment = VerticalAlignment.Center;
        for (var index = 0; index < cells.Length; index++)
        {
            if (cells[index] != null)
                FillCell(table, row, index, cells[index]!, bold, context);
        }
        return row;
    }

    private static void FillCell(Table table, Row row, int index, string text, bool bold, string context)
    {
        var width = table.Columns[index].Width.Point - table.LeftPadding.Point - table.RightPadding.Point;
        AddLines(row.Cells[index].AddParagraph(), ReportText.Cell(text, BodyFont(bold), width, context));
    }

    private static void AddLines(Paragraph paragraph, IReadOnlyList<string> lines)
    {
        for (var line = 0; line < lines.Count; line++)
        {
            if (line > 0)
                paragraph.AddLineBreak();
            paragraph.AddText(lines[line]);
        }
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
        $"{Number(plate.Bounds.Width)} x {Number(plate.Bounds.Length)} {snapshot.Units}";

    private static string Number(double value) => value.ToString("0.####", Invariant);

    private static string Percent(double value) => (value * 100).ToString("0.0", Invariant) + "%";
}
