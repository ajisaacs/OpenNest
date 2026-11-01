using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;

namespace OpenNest.WinForms.Tests.Controls;

public class BrowsePagerTests
{
    private enum Field { Name, Size }

    private enum OtherField { Size }

    [Fact]
    public void LeftClickOnASortColumnSortsOnTheServerAndReversesOnTheSameColumn() => RunSta(() =>
    {
        using var host = new Host();

        host.Click(host.SizeColumn, MouseButtons.Left);

        Assert.Equal(1, host.Session.Queries);
        Assert.Equal(Field.Size, host.Session.Sort);
        Assert.False(host.Session.Descending);
        Assert.Equal(1, host.Populated);

        host.Click(host.SizeColumn, MouseButtons.Left);

        Assert.Equal(2, host.Session.Queries);
        Assert.True(host.Session.Descending);
    });

    [Fact]
    public void OtherButtonsDoNotSort() => RunSta(() =>
    {
        using var host = new Host();

        host.Click(host.SizeColumn, MouseButtons.Right);
        host.Click(host.SizeColumn, MouseButtons.Middle);

        Assert.Equal(0, host.Session.Queries);
        Assert.Equal(Field.Name, host.Session.Sort);
    });

    [Fact]
    public void ColumnsWithoutThisListsSortFieldDoNotSort() => RunSta(() =>
    {
        using var host = new Host();

        host.Click(host.UntaggedColumn, MouseButtons.Left);
        host.Click(host.ForeignColumn, MouseButtons.Left);

        Assert.Equal(0, host.Session.Queries);
        Assert.Equal(Field.Name, host.Session.Sort);
    });

    private static void RunSta(Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(30), "The STA test did not complete.");

    private sealed class Host : IDisposable
    {
        private readonly DataGridView grid = new();
        private readonly Label summary = new();
        private readonly Button previous = new();
        private readonly Button next = new();
        private readonly BrowsePager<Page, Field> pager;

        public Host()
        {
            UntaggedColumn = grid.Columns.Add("untagged", "Untagged");
            SizeColumn = grid.Columns.Add("size", "Size");
            ForeignColumn = grid.Columns.Add("foreign", "Foreign");
            grid.Columns[SizeColumn].Tag = Field.Size;
            grid.Columns[ForeignColumn].Tag = OtherField.Size;
            pager = new BrowsePager<Page, Field>(
                Session, grid, summary, previous, next, "the test list")
            {
                Populate = _ => Populated++,
            };
        }

        public TestSession Session { get; } = new();

        public int UntaggedColumn { get; }

        public int SizeColumn { get; }

        public int ForeignColumn { get; }

        public int Populated { get; private set; }

        public void Click(int column, MouseButtons button)
        {
            var mouse = new MouseEventArgs(button, 1, 0, 0, 0);
            pager.SortByHeaderAsync(new DataGridViewCellMouseEventArgs(column, -1, 0, 0, mouse))
                .GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Session.Dispose();
            grid.Dispose();
            summary.Dispose();
            previous.Dispose();
            next.Dispose();
        }
    }

    private sealed class Page : IBrowsePage
    {
        public int Offset => 0;

        public int Total => 0;

        public int ItemCount => 0;
    }

    private sealed class TestSession : BrowseSession<Page, Field>
    {
        public TestSession()
            : base(pageSize: 10, maxPageSize: 100, Field.Name)
        {
        }

        public int Queries { get; private set; }

        public override string Summary => "";

        protected override Task<Page> QueryAsync(CancellationToken cancellationToken)
        {
            Queries++;
            return Task.FromResult(new Page());
        }
    }
}
