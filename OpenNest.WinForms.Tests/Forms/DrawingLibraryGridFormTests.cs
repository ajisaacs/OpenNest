using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Data;
using OpenNest.Forms;
using OpenNest.IO;
using static OpenNest.WinForms.Tests.FormTesting;

namespace OpenNest.WinForms.Tests.Forms;

public class DrawingLibraryGridFormTests
{
    [Fact]
    public void CheckAllPages_ShowsEveryDrawing_AndListsTheOnesThatFail()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PagedLibrary();
                for (var i = 0; i < DrawingLibraryGridForm.PageSize; i++)
                    library.Add($"RECT-{i:00}", Rectangles(1));
                // Two equal outlines once froze the preview's label search.
                library.Add("TWIN", Rectangles(2));
                var missing = library.Add("MISSING", null);

                using var form = new DrawingLibraryGridForm(library, "http://library.test:8090");
                Show(form);
                Run(form.CheckAllPagesAsync());

                Assert.Equal(DrawingLibraryGridForm.PageSize + 1, form.DrawingsShown);
                var problem = Assert.Single(form.Problems);
                Assert.Same(missing, problem.Record);
                Assert.Equal("Revision not on the server", problem.Problem);
                Assert.Equal(new[] { 0, DrawingLibraryGridForm.PageSize }, library.Offsets.Skip(library.Offsets.Count - 2));
            },
            TimeSpan.FromSeconds(60),
            "The drawing library grid check did not complete.");
    }

    // A faulted task rethrows here, so a failed check reports its own error instead of a timeout.
    private static void Run(Task task)
    {
        PumpUntil(() => task.IsCompleted, "the check");
        task.GetAwaiter().GetResult();
    }

    // Side-by-side 6 x 4 rectangles: a ridge, not a point, is clearest of their edges.
    private static Drawing Rectangles(int count)
    {
        var program = new Program();
        for (var i = 0; i < count; i++)
        {
            var x = i * 7;
            program.Codes.Add(new RapidMove(x, 0));
            program.Codes.Add(new LinearMove(x + 6, 0));
            program.Codes.Add(new LinearMove(x + 6, 4));
            program.Codes.Add(new LinearMove(x, 4));
            program.Codes.Add(new LinearMove(x, 0));
        }
        return new Drawing("rectangles", program);
    }

    private sealed class PagedLibrary : IDrawingLibrary
    {
        private readonly List<DrawingLibraryRecord> records = new();
        private readonly Dictionary<Guid, byte[]> packages = new();

        public List<int> Offsets { get; } = new();

        public DrawingLibraryRecord Add(string name, Drawing drawing)
        {
            var record = new DrawingLibraryRecord { Id = Guid.NewGuid(), Revision = 1, Name = name };
            records.Add(record);
            if (drawing != null)
                packages[record.Id] = DrawingPackage.Write(drawing, Units.Inches);
            return record;
        }

        public Task<DrawingLibraryPage> QueryAsync(DrawingLibraryQuery query, CancellationToken cancellationToken = default)
        {
            Offsets.Add(query.Offset);
            return Task.FromResult(new DrawingLibraryPage
            {
                Items = records.Skip(query.Offset).Take(query.Limit).ToArray(),
                Total = records.Count,
                Offset = query.Offset,
                Limit = query.Limit,
            });
        }

        public Task<byte[]?> GetRevisionPackageAsync(Guid id, int revision, CancellationToken cancellationToken = default) =>
            Task.FromResult(packages.TryGetValue(id, out var package) ? package : null);
    }
}
