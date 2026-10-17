using System.Reflection;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Data;
using OpenNest.Forms;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.WinForms.Tests.Forms;

public class SavedNestsFormTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public void HighlightedNestShowsItsPlatesAndDrawings_AndEnterOpensIt()
    {
        StaTestThread.Run(
            () =>
            {
                var first = Record("First job");
                var second = Record("Second job");
                var repository = new FakeRepository(first, second);
                repository.Files[first.Id] = NestFile((Quantity: 2, Parts: 3));
                repository.Files[second.Id] = NestFile((Quantity: 1, Parts: 1));

                using var form = new SavedNestsForm(repository, "http://nest-server:5000");
                form.Show();
                var nests = Field<DataGridView>(form, "nestGrid");
                var plates = Field<DataGridView>(form, "platesGrid");
                var drawings = Field<DataGridView>(form, "drawingsGrid");

                PumpUntil(() => nests.Rows.Count == 2, "the nest page");
                Assert.Equal("First job", nests.Rows[0].Cells[0].Value);
                Assert.Equal("1", nests.Rows[0].HeaderCell.Value);
                Assert.Contains("http://nest-server:5000", form.Text);
                Assert.Contains("Showing 1-2 of 2 nests", form.Text);

                PumpUntil(() => plates.Rows.Count == 1, "the first nest's plates");
                Assert.Equal(2, plates.Rows[0].Cells["Duplicates"].Value);
                Assert.Equal(3, plates.Rows[0].Cells["Parts"].Value);
                Assert.Equal("Plate size (in)", plates.Columns["PlateSize"].HeaderText);
                Assert.Equal(6, drawings.Rows[0].Cells["Nested"].Value);

                nests.CurrentCell = nests.Rows[1].Cells[0];
                PumpUntil(
                    () => plates.Rows.Count == 1 && Equals(plates.Rows[0].Cells["Duplicates"].Value, 1),
                    "the second nest's plates");
                Assert.Equal(1, drawings.Rows[0].Cells["Nested"].Value);

                // Selecting a row does not guarantee keyboard focus after pumping UI events.
                Assert.True(nests.Focus());
                Assert.True(nests.Focused);
                Assert.True(ProcessKey(form, Keys.Enter));
                Assert.Equal(DialogResult.OK, form.DialogResult);
                Assert.Equal(second.Id, form.SelectedId);
            },
            TestTimeout,
            "The saved-nest dialog test did not complete.");
    }

    [Fact]
    public void PlatePreview_StepsThroughThePlates_AndFollowsThePlatesTable()
    {
        StaTestThread.Run(
            () =>
            {
                var job = Record("Two plates");
                var repository = new FakeRepository(job);
                repository.Files[job.Id] = NestFile((Quantity: 1, Parts: 3), (Quantity: 1, Parts: 1));

                using var form = new SavedNestsForm(repository);
                form.Show();
                var plates = Field<DataGridView>(form, "platesGrid");
                var preview = Field<OpenNest.Controls.PlateView>(form, "platePreview");
                var label = Field<Label>(form, "plateLabel");
                var previous = Field<Button>(form, "previousPlateButton");
                var next = Field<Button>(form, "nextPlateButton");

                PumpUntil(() => plates.Rows.Count == 2, "the nest's plates");
                Assert.True(preview.Visible);
                Assert.Equal("Plate 1 of 2", label.Text);
                Assert.Equal(3, preview.Plate.Parts.Count);
                Assert.False(previous.Enabled);
                Assert.True(next.Enabled);

                next.PerformClick();
                Assert.Equal("Plate 2 of 2", label.Text);
                Assert.Single(preview.Plate.Parts);
                Assert.Equal(1, plates.CurrentRow!.Index);
                Assert.Equal(1, plates.SelectedRows[0].Index);
                Assert.True(previous.Enabled);
                Assert.False(next.Enabled);

                plates.CurrentCell = plates.Rows[0].Cells[0];
                Assert.Equal("Plate 1 of 2", label.Text);
                Assert.Equal(3, preview.Plate.Parts.Count);
            },
            TestTimeout,
            "The saved-nest dialog test did not complete.");
    }

    [Fact]
    public void MissingArchive_ShowsTheErrorInTheDetailsLine_AndEscapeCancels()
    {
        StaTestThread.Run(
            () =>
            {
                var gone = Record("Deleted elsewhere");
                using var form = new SavedNestsForm(new FakeRepository(gone));
                form.Show();
                var status = Field<Label>(form, "detailsStatus");

                PumpUntil(() => status.Text.Contains("no longer exists"), "the missing-nest message");
                Assert.Equal(0, Field<DataGridView>(form, "platesGrid").Rows.Count);
                Assert.False(Field<OpenNest.Controls.PlateView>(form, "platePreview").Visible);
                Assert.Equal("", Field<Label>(form, "plateLabel").Text);

                Assert.True(ProcessKey(form, Keys.Escape));
                Assert.Equal(DialogResult.Cancel, form.DialogResult);
                Assert.Equal(Guid.Empty, form.SelectedId);
            },
            TestTimeout,
            "The saved-nest dialog test did not complete.");
    }

    [Fact]
    public void OpensAsALargeNormalWindowInsideTheOwnersScreen()
    {
        StaTestThread.Run(
            () =>
            {
                using var owner = new Form { StartPosition = FormStartPosition.Manual, Bounds = new(40, 40, 400, 300) };
                owner.Show();
                using var form = new SavedNestsForm(new FakeRepository());
                form.Show(owner);
                var area = Screen.FromControl(owner).WorkingArea;

                // A maximized dialog with fixed MaximizedBounds landed off-screen on a secondary
                // monitor, hiding the modal dialog behind a disabled main window.
                Assert.Equal(FormWindowState.Normal, form.WindowState);
                Assert.True(area.Contains(form.Bounds), $"Dialog bounds {form.Bounds} are outside the working area {area}.");
                Assert.True(
                    form.Width >= area.Width * 8 / 10 && form.Height >= area.Height * 8 / 10,
                    $"Dialog size {form.Size} is small for the working area {area}.");
            },
            TestTimeout,
            "The saved-nest dialog test did not complete.");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void NestInformation_SavesOnlyOnAccept_AndRefusesStaleWrites(bool accept, bool conflict)
    {
        StaTestThread.Run(() =>
        {
            // PerformClick outside Application.Run needs the real UI dispatcher installed.
            using var context = new WindowsFormsSynchronizationContext();
            var record = Record("Current name");
            record.Revision = 3;
            record.Comments = "Current metadata notes";
            record.Status = NestStatus.ToBeCut;
            record.Material = "Aluminum";
            record.Thickness = 0.375;
            record.DateCreated = new DateTime(2026, 1, 1);
            var repository = new FakeRepository(record) { Conflict = conflict };
            repository.Files[record.Id] = NestFile((Quantity: 2, Parts: 3));
            record.FileSize = repository.Files[record.Id].Length;
            var original = repository.Files[record.Id].ToArray();
            using var form = new SavedNestsForm(repository, "http://nest-server:5000");
            form.Show();
            PumpUntil(() => Field<DataGridView>(form, "nestGrid").Rows.Count == 1, "the nest page");
            SynchronizationContext.SetSynchronizationContext(context);
            var shown = false;
            form.ShowNestInfoDialog = dialog =>
            {
                shown = true;
                Assert.Equal(record.Name, dialog.NestName);
                Assert.Equal(record.Comments, dialog.Notes);
                Assert.Equal(NestStatus.ToBeCut, dialog.Status);
                Assert.Equal("Aluminum", dialog.MaterialName);
                Assert.Equal(0.375, dialog.Thickness);
                Assert.Equal(new DateTime(2026, 1, 1), dialog.DateCreated);
                dialog.Customer = "Updated customer";
                dialog.Notes = "Updated notes";
                dialog.Status = NestStatus.HasBeenCut;
                dialog.MadeBy = "Operator";
                return accept ? DialogResult.OK : DialogResult.Cancel;
            };
            var button = Field<Button>(form, "infoButton");
            Assert.True(button.Enabled);
            Assert.Contains(Field<DataGridView>(form, "nestGrid").ContextMenuStrip!.Items.Cast<ToolStripItem>(),
                item => item.Text == "Nest Information...");
            button.PerformClick();
            PumpUntil(() => shown && form.Enabled, "the information edit");
            Assert.True(shown);
            Assert.True(form.Enabled);
            Assert.Equal(DialogResult.None, form.DialogResult);
            Assert.Equal(Guid.Empty, form.SelectedId);
            Assert.Equal(accept ? 1 : 0, repository.UpdateAttempts);
            if (accept && !conflict)
            {
                Assert.Equal(3, repository.ExpectedRevision);
                Assert.Equal(4, record.Revision);
                Assert.Equal("Updated customer", record.Customer);
                Assert.Equal("Updated notes", record.Comments);
                using var stream = new MemoryStream(repository.Files[record.Id]);
                var nest = new NestReader(stream).Read();
                Assert.Equal("Updated customer", nest.Customer);
                Assert.Equal("Updated notes", nest.Notes);
                Assert.Equal(NestStatus.HasBeenCut, nest.Status);
                Assert.Equal("Operator", nest.MadeBy);
                Assert.Equal("Aluminum", nest.Material.Name);
                Assert.Equal(0.375, nest.Thickness);
                Assert.Equal(new DateTime(2026, 1, 1), nest.DateCreated);
                Assert.Equal("Aluminum", record.Material);
                Assert.Equal(0.375, record.Thickness);
                Assert.Equal(new DateTime(2026, 1, 1), record.DateCreated);
                Assert.Single(nest.Plates);
                Assert.Equal(2, nest.Plates[0].Quantity);
                Assert.Equal(3, nest.Plates[0].Parts.Count);
                Assert.Equal("Updated customer", Field<DataGridView>(form, "nestGrid").Rows[0].Cells["Customer"].Value);
            }
            else
            {
                Assert.Equal(original, repository.Files[record.Id]);
                Assert.Equal(3, record.Revision);
                if (conflict)
                    Assert.Contains("409", Field<Label>(form, "summaryLabel").Text);
            }
        }, TestTimeout, "The information edit did not complete.");
    }

    [Fact]
    public void NestInformation_PendingOperationBlocksReentryAndClose_AndRefreshesWarmedDetails()
    {
        StaTestThread.Run(() =>
        {
            // PerformClick outside Application.Run needs the real UI dispatcher installed.
            using var context = new WindowsFormsSynchronizationContext();
            var record = Record("Busy job");
            record.Revision = 1;
            var repository = new FakeRepository(record);
            repository.Files[record.Id] = NestFile((Quantity: 1, Parts: 2));
            record.FileSize = repository.Files[record.Id].Length;
            using var form = new SavedNestsForm(repository, "http://nest-server:5000");
            form.Show();
            PumpUntil(() => Field<DataGridView>(form, "platesGrid").Rows.Count == 1, "warmed details");
            var oldDetails = Field<NestDetailsSession>(form, "details");
            repository.ReadGate = new TaskCompletionSource<bool>();
            repository.SaveGate = new TaskCompletionSource<bool>();
            SynchronizationContext.SetSynchronizationContext(context);
            var shown = false;
            form.ShowNestInfoDialog = dialog =>
            {
                shown = true;
                dialog.Notes = "Saved after wait";
                return DialogResult.OK;
            };
            var operation = form.EditSelectedInfoAsync();
            Assert.False(operation.IsCompleted);
            Assert.False(form.Enabled);
            Assert.False(Field<ToolStripMenuItem>(form, "infoMenuItem").Enabled);
            form.Close();
            Assert.False(form.IsDisposed);
            Assert.True(form.EditSelectedInfoAsync().IsCompletedSuccessfully);
            Assert.False(shown);
            repository.ReadGate.SetResult(true);
            PumpUntil(() => repository.UpdateAttempts == 1, "pending save");
            Assert.True(shown);
            Assert.False(operation.IsCompleted);
            Assert.False(form.Enabled);
            repository.SaveGate.SetResult(true);
            PumpUntil(() => operation.IsCompleted, "completed save");
            Assert.Null(operation.Exception);
            Assert.True(form.Enabled);
            Assert.NotSame(oldDetails, Field<NestDetailsSession>(form, "details"));
            PumpUntil(() => Field<DataGridView>(form, "platesGrid").Rows.Count == 1, "reloaded details");
            Assert.Equal("Saved after wait", record.Comments);
        }, TestTimeout, "The pending information edit did not complete.");
    }

    private static NestRecord Record(string name) =>
        new() { Id = Guid.NewGuid(), Name = name, SavedAt = DateTime.Now };

    private static byte[] NestFile(params (int Quantity, int Parts)[] plates)
    {
        var nest = new Nest("Job") { Units = Units.Inches };
        var program = new OpenNest.CNC.Program();
        program.Codes.Add(new RapidMove(new Vector(0, 0)));
        program.Codes.Add(new LinearMove(new Vector(10, 0)));
        program.Codes.Add(new LinearMove(new Vector(10, 10)));
        program.Codes.Add(new LinearMove(new Vector(0, 10)));
        program.Codes.Add(new LinearMove(new Vector(0, 0)));
        var drawing = new Drawing("Square", program);
        drawing.Quantity.Required = 10;
        nest.Drawings.Add(drawing);

        foreach (var (quantity, parts) in plates)
        {
            var plate = nest.CreatePlate();
            plate.Size = new OpenNest.Geometry.Size(48, 96);
            plate.Quantity = quantity;
            for (var i = 0; i < parts; i++)
                plate.Parts.Add(new Part(drawing, new Vector(i * 12, 0)));
        }

        using var stream = new MemoryStream();
        new NestWriter(nest).Write(stream);
        return stream.ToArray();
    }

    private static void PumpUntil(Func<bool> condition, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }

        Assert.True(condition(), $"Timed out waiting for {what}.");
    }

    private static bool ProcessKey(Form form, Keys key)
    {
        var method = form.GetType().GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (bool)method.Invoke(form, new object[] { new Message(), key })!;
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    /// <summary>Serves one page of the given records and archives by id.</summary>
    private sealed class FakeRepository : INestRepository
    {
        private readonly NestRecord[] _records;

        public FakeRepository(params NestRecord[] records) => _records = records;

        public TaskCompletionSource<bool>? ReadGate { get; set; }
        public TaskCompletionSource<bool>? SaveGate { get; set; }
        public bool Conflict { get; set; }
        public int UpdateAttempts { get; private set; }
        public int ExpectedRevision { get; private set; }
        public Dictionary<Guid, byte[]> Files { get; } = new();

        public Task<byte[]?> GetRevisionFileAsync(Guid id, int revisionNumber, CancellationToken cancellationToken = default) =>
            GetFileAsync(id, cancellationToken);

        public Task<NestRevisionPage> GetRevisionsAsync(Guid id, int offset = 0, int limit = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NestRevisionPage());

        public async Task<NestRecord> UpdateFileAsync(Guid id, byte[] bytes, NestRecord metadata, int expectedRevision,
            CancellationToken cancellationToken = default)
        {
            UpdateAttempts++;
            ExpectedRevision = expectedRevision;
            if (SaveGate != null) await SaveGate.Task;
            if (Conflict) throw new IOException("409: changed on another PC");
            var current = _records.Single(r => r.Id == id);
            Assert.Equal(current.Revision, expectedRevision);
            current.Revision++;
            current.Name = metadata.Name;
            current.Customer = metadata.Customer;
            current.Comments = metadata.Comments;
            current.Material = metadata.Material;
            current.Thickness = metadata.Thickness;
            current.Status = metadata.Status;
            current.MadeBy = metadata.MadeBy;
            current.DateModified = metadata.DateModified;
            current.DateCreated = metadata.DateCreated;
            current.FileSize = bytes.Length;
            Files[id] = bytes;
            return current;
        }

        public Task<NestPage> QueryAsync(NestQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NestPage
            {
                Items = _records,
                Total = _records.Length,
                Offset = query.Offset,
                Limit = query.Limit,
            });

        public Task<byte[]?> GetFileAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Files.TryGetValue(id, out var file) ? file : null);

        public Task<IReadOnlyList<NestRecord>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<NestRecord?> GetMetadataAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (ReadGate != null) await ReadGate.Task;
            return _records.SingleOrDefault(r => r.Id == id);
        }

        public Task<NestRecord> UploadAsync(byte[] nestFile, NestRecord record, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<NestRecord> UpdateFileAsync(
            Guid id, byte[] nestFile, NestRecord record, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<NestRecord> UpdateMetadataAsync(Guid id, NestRecord record, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
