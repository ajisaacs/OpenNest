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

        public Dictionary<Guid, byte[]> Files { get; } = new();

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

        public Task<NestRecord?> GetMetadataAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
