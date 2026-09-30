using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using OpenNest.CNC;
using OpenNest.Data;
using OpenNest.Forms;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.WinForms.Tests.Forms;

[Collection("Fill operation lifetime")]
public class PlateExportTests
{
    [Fact]
    public void CancelWritesNothingAndNotifiesNothing() => RunSta(() =>
    {
        using var files = new ExportFiles();
        using var form = ShowJob();
        var path = files.PathFor("cancel.txt");
        form.DialogResults.Enqueue(null);

        Assert.False(form.Export());

        Assert.Single(form.DialogRequests);
        Assert.Empty(form.WriteRequests);
        Assert.Empty(form.Failures);
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(files.DirectoryPath));
    });

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    public void CoordinatesPreserveFormatAndReleaseFile(string cultureName) => RunSta(() =>
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            using var files = new ExportFiles();
            using var form = ShowJob();
            var path = files.PathFor("coordinates.txt");
            var expected = ExpectedLines(form.PlateView.Plate);
            form.DialogResults.Enqueue((path, 3));

            Assert.True(form.Export());

            Assert.True(File.Exists(path));
            Assert.Equal(expected, File.ReadAllLines(path));
            Assert.Empty(form.Failures);
            AssertFileReleased(path);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    });

    [Fact]
    public void JpegRendersShownPlateAndReleasesFile() => RunSta(() =>
    {
        using var files = new ExportFiles();
        using var form = ShowJob();
        var path = files.PathFor("plate.jpg");
        Assert.True(form.Visible);
        Assert.True(form.PlateView.IsHandleCreated);
        Assert.True(form.PlateView.Width > 0);
        Assert.True(form.PlateView.Height > 0);
        form.DialogResults.Enqueue((path, 2));

        Assert.True(form.Export());

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 2);
        Assert.Equal(0xff, bytes[0]);
        Assert.Equal(0xd8, bytes[1]);
        using (var image = new Bitmap(path))
        {
            Assert.Equal(System.Drawing.Imaging.ImageFormat.Jpeg.Guid, image.RawFormat.Guid);
            Assert.Equal(form.PlateView.Width, image.Width);
            Assert.Equal(form.PlateView.Height, image.Height);
        }
        Assert.Empty(form.Failures);
        AssertFileReleased(path);
    });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MissingParentDirectoryReportsOneFailure(int filterIndex) => RunSta(() =>
    {
        using var files = new ExportFiles();
        using var form = ShowJob();
        var path = files.PathFor(Path.Combine("missing", "plate"));
        form.DialogResults.Enqueue((path, filterIndex));

        Assert.False(form.Export());

        var failure = Assert.Single(form.Failures);
        Assert.Equal(path, failure.Destination);
        Assert.False(string.IsNullOrWhiteSpace(failure.Error.Message));
        Assert.Single(form.WriteRequests);
        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
    });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FailureAfterPartialWriteIsNotSuccess(int filterIndex) => RunSta(() =>
    {
        using var files = new ExportFiles();
        using var form = ShowJob();
        var path = files.PathFor("partial-export");
        var error = new IOException("Injected failure after partial write.");
        form.WriteAction = (_, destination, _) =>
        {
            File.WriteAllText(destination, "partial");
            throw error;
        };
        form.DialogResults.Enqueue((path, filterIndex));

        Assert.False(form.Export());

        var failure = Assert.Single(form.Failures);
        Assert.Equal(path, failure.Destination);
        Assert.Same(error, failure.Error);
        Assert.Equal("partial", File.ReadAllText(path));
        AssertFileReleased(path);
    });

    [Fact]
    public void FlushFailureDuringWriterDisposalIsNotSuccess() => RunSta(() =>
    {
        using var files = new ExportFiles();
        using var form = ShowJob();
        var path = files.PathFor("flush-failure.txt");
        FlushFailureStream? stream = null;
        var writeBodyCompleted = false;
        form.WriteAction = (_, destination, _) =>
        {
            using (stream = new FlushFailureStream(destination))
            using (var writer = new StreamWriter(stream))
            {
                writer.WriteLine("partial");
                writeBodyCompleted = true;
                Assert.False(stream.FlushAttempted);
            }
        };
        form.DialogResults.Enqueue((path, 3));

        Assert.False(form.Export());

        Assert.True(writeBodyCompleted);
        Assert.NotNull(stream);
        Assert.True(stream.FlushAttempted);
        Assert.True(stream.Disposed);
        var failure = Assert.Single(form.Failures);
        Assert.Equal(path, failure.Destination);
        Assert.IsType<IOException>(failure.Error);
        Assert.Equal("Injected flush failure during disposal.", failure.Error.Message);
        Assert.Equal("partial" + Environment.NewLine, File.ReadAllText(path));
        AssertFileReleased(path);
    });

    [Fact]
    public void UnsupportedFilterReportsFailureWithoutCreatingFile() => RunSta(() =>
    {
        using var files = new ExportFiles();
        using var form = ShowJob();
        var path = files.PathFor("unsupported");

        Assert.False(form.TryExportPlate(form.PlateView.Plate, path, 99));

        var failure = Assert.Single(form.Failures);
        Assert.Equal(path, failure.Destination);
        var error = Assert.IsType<ArgumentException>(failure.Error);
        Assert.Equal("filterIndex", error.ParamName);
        Assert.False(File.Exists(path));
        Assert.Empty(form.DialogRequests);
    });

    [Fact]
    public void CoordinateExportLeavesDocumentAndJobStateUntouched() => RunSta(() =>
    {
        using var files = new ExportFiles();
        using var form = ShowJob();
        form.PlateManager.LoadAt(1);
        var nest = form.Nest;
        var savedPath = files.PathFor("saved-job.nest");
        form.Document.SaveAs(savedPath);
        form.Document.BindRemote(Guid.NewGuid(), "https://nest-storage.example.test");
        var remoteSession = (NestSaveSession)typeof(Document)
            .GetField("remoteSession", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form.Document)!;
        var remoteId = form.Document.RemoteId;
        var serverUrl = remoteSession.ServerUrl;
        var lastSaveDate = form.Document.LastSaveDate;
        var lastModified = nest.DateLastModified;
        var name = nest.Name;
        var title = form.Text;
        var savedBytes = File.ReadAllBytes(savedPath);
        var plates = nest.Plates.ToArray();
        var plateQuantities = plates.Select(plate => plate.Quantity).ToArray();
        var drawings = nest.Drawings.ToArray();
        var drawingQuantities = drawings.Select(drawing => drawing.Quantity).ToArray();
        var parts = plates.SelectMany(plate => plate.Parts).ToArray();
        var poses = parts.Select(part => (part.Location.X, part.Location.Y, part.Rotation)).ToArray();
        var partCounts = plates.Select(plate => plate.Parts.Count).ToArray();
        var layoutDirty = form.PlateView.Parts.Select(part => part.IsDirty).ToArray();
        var plateChanged = 0;
        form.PlateChanged += (_, _) => plateChanged++;
        var path = files.PathFor("read-only-export.txt");
        form.DialogResults.Enqueue((path, 3));

        Assert.True(form.Export());

        Assert.Equal(ExpectedLines(plates[1]), File.ReadAllLines(path));
        Assert.Equal(savedPath, form.Document.LastSavePath);
        Assert.True(form.Document.HasSavePath);
        Assert.Equal(savedBytes, File.ReadAllBytes(savedPath));
        Assert.Equal(remoteId, form.Document.RemoteId);
        Assert.Equal(serverUrl, remoteSession.ServerUrl);
        Assert.Equal(lastSaveDate, form.Document.LastSaveDate);
        Assert.Equal(lastModified, nest.DateLastModified);
        Assert.Equal(name, nest.Name);
        Assert.Equal(title, form.Text);
        Assert.Equal(1, form.PlateManager.CurrentIndex);
        Assert.Equal(plates, nest.Plates.ToArray());
        Assert.Equal(plateQuantities, nest.Plates.Select(plate => plate.Quantity).ToArray());
        // Drawing.Equals compares names, so reference identity is asserted explicitly.
        Assert.Equal(drawings, nest.Drawings.ToArray());
        foreach (var (expected, actual) in drawings.Zip(nest.Drawings))
            Assert.Same(expected, actual);
        Assert.Equal(drawingQuantities, nest.Drawings.Select(drawing => drawing.Quantity).ToArray());
        Assert.Equal(partCounts, nest.Plates.Select(plate => plate.Parts.Count).ToArray());
        Assert.Equal(parts, nest.Plates.SelectMany(plate => plate.Parts).ToArray());
        Assert.Equal(poses, parts.Select(part => (part.Location.X, part.Location.Y, part.Rotation)).ToArray());
        Assert.Equal(layoutDirty, form.PlateView.Parts.Select(part => part.IsDirty).ToArray());
        Assert.Equal(0, plateChanged);
        Assert.Empty(form.Failures);
    });

    [Fact]
    public void ExportAllWritesEveryPlateOnceAndAdvancesToLast() => RunSta(() =>
    {
        using var files = new ExportFiles();
        using var form = ShowJob();
        // EditNestForm adds an empty sentinel, and LoadNext visits it too. Preserve that behavior.
        var plates = form.Nest.Plates.ToArray();
        Assert.Equal(4, plates.Length);
        Assert.All(plates.Take(3), plate => Assert.NotEmpty(plate.Parts));
        Assert.Empty(plates[^1].Parts);
        var paths = plates.Select((_, index) => files.PathFor($"plate-{index + 1}.txt")).ToArray();
        var expected = plates.Select(ExpectedLines).ToArray();
        foreach (var path in paths)
            form.DialogResults.Enqueue((path, 3));
        form.PlateManager.LoadAt(1); // Export All must start at the first, not the selected plate.

        form.ExportAll();

        Assert.Equal(plates, form.WriteRequests.Select(request => request.Plate).ToArray());
        Assert.Equal(paths, form.WriteRequests.Select(request => request.Destination).ToArray());
        Assert.All(form.WriteRequests, request => Assert.Equal(3, request.FilterIndex));
        Assert.Equal(Enumerable.Range(1, plates.Length).Select(index => $"export job-P{index}"),
            form.DialogRequests);
        Assert.Empty(form.DialogResults);
        Assert.Empty(form.Failures);
        Assert.Equal(plates.Length - 1, form.PlateManager.CurrentIndex);
        Assert.Same(plates[^1], form.PlateView.Plate);
        for (var i = 0; i < paths.Length; i++)
        {
            Assert.Equal(expected[i], File.ReadAllLines(paths[i]));
            AssertFileReleased(paths[i]);
        }
    });

    [Fact]
    public void ExportAllStopsWhenSecondDialogIsCanceled() => RunSta(() =>
    {
        using var files = new ExportFiles();
        using var form = ShowJob();
        var first = files.PathFor("plate-1.txt");
        var second = files.PathFor("plate-2.txt");
        var third = files.PathFor("plate-3.txt");
        form.DialogResults.Enqueue((first, 3));
        form.DialogResults.Enqueue(null);
        form.DialogResults.Enqueue((third, 3));

        form.ExportAll();

        Assert.Equal(new[] { "export job-P1", "export job-P2" }, form.DialogRequests);
        Assert.Single(form.DialogResults);
        Assert.Same(form.Nest.Plates[0], Assert.Single(form.WriteRequests).Plate);
        Assert.Equal(ExpectedLines(form.Nest.Plates[0]), File.ReadAllLines(first));
        Assert.False(File.Exists(second));
        Assert.False(File.Exists(third));
        Assert.Equal(1, form.PlateManager.CurrentIndex);
        Assert.Same(form.Nest.Plates[1], form.PlateView.Plate);
        Assert.Empty(form.Failures);
    });

    [Fact]
    public void ExportAllStopsAtFirstWriteFailure() => RunSta(() =>
    {
        using var files = new ExportFiles();
        using var form = ShowJob();
        var first = files.PathFor(Path.Combine("missing", "plate-1.txt"));
        var second = files.PathFor("plate-2.txt");
        form.DialogResults.Enqueue((first, 3));
        form.DialogResults.Enqueue((second, 3));

        form.ExportAll();

        Assert.Equal("export job-P1", Assert.Single(form.DialogRequests));
        Assert.Single(form.DialogResults);
        Assert.Same(form.Nest.Plates[0], Assert.Single(form.WriteRequests).Plate);
        var failure = Assert.Single(form.Failures);
        Assert.Equal(first, failure.Destination);
        Assert.IsType<DirectoryNotFoundException>(failure.Error);
        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.Equal(0, form.PlateManager.CurrentIndex);
        Assert.Same(form.Nest.Plates[0], form.PlateView.Plate);
    });

    private static TestEditNestForm ShowJob()
    {
        var form = new TestEditNestForm(CreateJob());
        try
        {
            form.PlateView.SetOverlapAutoCheck(null);
            form.Show();
        }
        catch
        {
            form.Dispose();
            throw;
        }

        return form;
    }

    private static Nest CreateJob()
    {
        var nest = new Nest("export job") { Units = Units.Inches };
        var drawing = new Drawing("rect", Rectangle());
        drawing.Source.Path = @"C:\drawings\rect.dxf";
        drawing.Source.Offset = new Vector(1.234567891, -0.987654321);
        drawing.Quantity.Required = 12;
        nest.Drawings.Add(drawing);
        var spare = new Drawing("spare", Rectangle());
        spare.Source.Path = @"C:\drawings\spare.dxf";
        spare.Source.Offset = new Vector(-0.25, 0.5);
        spare.Quantity.Required = 5;
        nest.Drawings.Add(spare);
        for (var i = 0; i < 3; i++)
        {
            var plate = new Plate(24, 48) { Quantity = i + 1 };
            var part = new Part(drawing, new Vector(6.123456789 + i, 8.987654321 + i));
            part.Rotate(Angle.ToRadians(15.25 + i * 30));
            plate.Parts.Add(part);
            plate.Parts.Add(new Part(spare, new Vector(14.5 + i, 16.25 + i)));
            nest.Plates.Add(plate);
        }
        return nest;
    }

    private static Program Rectangle()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(4, 0));
        program.Codes.Add(new LinearMove(4, 2));
        program.Codes.Add(new LinearMove(0, 2));
        program.Codes.Add(new LinearMove(0, 0));
        return program;
    }

    private static string[] ExpectedLines(Plate plate) => plate.Parts.Select(part =>
    {
        var pt = part.BaseDrawing.Source.Offset.Rotate(part.Rotation);
        return string.Format("{0}|{1},{2}|{3}", part.BaseDrawing.Source.Path,
            System.Math.Round(part.Location.X - pt.X, 8),
            System.Math.Round(part.Location.Y - pt.Y, 8),
            Angle.ToDegrees(part.Rotation));
    }).ToArray();

    private static void AssertFileReleased(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(stream.CanRead);
    }

    private sealed class TestEditNestForm(Nest nest) : EditNestForm(nest)
    {
        public Queue<(string Destination, int FilterIndex)?> DialogResults { get; } = [];
        public List<string> DialogRequests { get; } = [];
        public List<(Plate Plate, string Destination, int FilterIndex)> WriteRequests { get; } = [];
        public List<(string Destination, Exception Error)> Failures { get; } = [];
        public System.Action<Plate, string, int>? WriteAction { get; set; }

        internal override (string Destination, int FilterIndex)? ShowPlateExportDialog(string suggestedFileName)
        {
            DialogRequests.Add(suggestedFileName);
            return DialogResults.Dequeue();
        }

        internal override void WritePlateExport(Plate plate, string destination, int filterIndex)
        {
            WriteRequests.Add((plate, destination, filterIndex));
            if (WriteAction != null)
                WriteAction(plate, destination, filterIndex);
            else
                base.WritePlateExport(plate, destination, filterIndex);
        }

        internal override void ReportPlateExportFailure(string destination, Exception error) =>
            Failures.Add((destination, error));
    }

    private sealed class FlushFailureStream(string path) : FileStream(path, FileMode.Create)
    {
        public bool FlushAttempted { get; private set; }
        public bool Disposed { get; private set; }

        public override void Flush()
        {
            FlushAttempted = true;
            base.Flush();
            throw new IOException("Injected flush failure during disposal.");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Disposed = true;
        }
    }

    private sealed class ExportFiles : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), $"opennest-plate-export-{Guid.NewGuid():N}");

        public ExportFiles() => Directory.CreateDirectory(DirectoryPath);

        public string PathFor(string name) => Path.Combine(DirectoryPath, name);

        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }

    private static void RunSta(System.Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(90)), "The STA test did not complete.");
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
