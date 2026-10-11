using System.Reflection;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

public class NestHistoryFormTests
{
    [Fact]
    public void HistoricalSelection_IsReadOnlyAndRestoreIsDistinctFromCurrent()
    {
        StaTestThread.Run(() =>
        {
            var repository = new Repository();
            using var form = new NestHistoryForm(repository, "http://shop", repository.Id);
            form.Show();
            Application.DoEvents();
            var grid = Field<DataGridView>(form, "revisions");
            Assert.Equal(2, grid.Rows.Count);
            Assert.True(grid.ReadOnly);
            Assert.Equal("Current", grid.Rows[0].Cells["State"].Value);
            Assert.False(Field<Button>(form, "restore").Enabled);
            grid.CurrentCell = grid.Rows[1].Cells[0];
            Assert.Equal("Historical", grid.SelectedRows[0].Cells["State"].Value);
            Assert.True(Field<Button>(form, "restore").Enabled);
            Assert.True(Field<Button>(form, "download").Enabled);
            Assert.Equal(0, repository.Writes);
            Assert.False(form.Changed);
            form.Close();
        }, TimeSpan.FromSeconds(30), "History form did not complete.");
    }

    [Fact]
    public void Recovery_IsSeparateViewWithExplicitRecoveryButton()
    {
        StaTestThread.Run(() =>
        {
            var repository = new Repository { Deleted = true };
            using var form = new NestHistoryForm(repository, "http://shop");
            form.Show();
            Application.DoEvents();
            Assert.Single(Field<DataGridView>(form, "records").Rows.Cast<DataGridViewRow>());
            Assert.True(Field<Button>(form, "recover").Visible);
            Assert.True(Field<Button>(form, "recover").Enabled);
            Assert.False(Field<Button>(form, "restore").Visible);
            Assert.Equal(0, repository.Writes);
            form.Close();
        }, TimeSpan.FromSeconds(30), "Recovery form did not complete.");
    }

    [Fact]
    public void SavedNests_VersionsTabFollowsSelectedNestWithoutWriting()
    {
        StaTestThread.Run(() =>
        {
            var repository = new Repository();
            using var browser = new SavedNestsForm(repository, "http://shop");
            browser.Show();
            Application.DoEvents();
            var tabs = Field<TabControl>(browser, "detailsTabs");
            Assert.Equal(new[] { "Plates", "Drawings", "Versions" },
                tabs.TabPages.Cast<TabPage>().Select(page => page.Text));
            tabs.SelectedIndex = 2;
            Application.DoEvents();
            var first = Field<NestHistoryForm>(browser, "historyView");
            Assert.NotNull(first);
            Assert.Same(tabs.SelectedTab, first.Parent);
            Assert.True(first.Visible);
            Assert.Equal(repository.Id, ((NestRecord)Field<DataGridView>(first, "revisions").Rows[0].Tag!).Id);
            Assert.False(Field<Button>(first, "restore").Enabled);

            var nests = Field<DataGridView>(browser, "nestGrid");
            nests.CurrentCell = nests.Rows[1].Cells[0];
            Application.DoEvents();
            var second = Field<NestHistoryForm>(browser, "historyView");
            Assert.True(first.IsDisposed);
            Assert.NotSame(first, second);
            Assert.Equal(repository.SecondId, ((NestRecord)Field<DataGridView>(second, "revisions").Rows[0].Tag!).Id);
            Assert.Equal(0, repository.Writes);
            browser.Close();
        }, TimeSpan.FromSeconds(30), "Embedded history did not follow the selected nest.");
    }

    [Fact]
    public void Document_BindsOpenedRevisionWithoutLegacyFallback()
    {
        var document = new Document { Nest = new Nest("Synthetic") };
        var id = Guid.NewGuid();
        document.BindRemote(id, "http://shop", 5);
        Assert.Equal(id, document.RemoteId);
        Assert.Equal(5, document.RemoteRevision);
        document.BindRemote(id, "http://shop");
        Assert.Equal(0, document.RemoteRevision);
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;

    private sealed class Repository : INestRepository
    {
        public Guid Id = Guid.NewGuid();
        public Guid SecondId = Guid.NewGuid();
        public bool Deleted;
        public int Writes;
        private NestRecord Record(int revision, Guid? id = null) => new() { Id = id ?? Id, Revision = revision, Deleted = Deleted, Name = "Synthetic" };
        public Task<NestRecord?> GetMetadataAsync(Guid id, CancellationToken ct = default) => Task.FromResult<NestRecord?>(Record(5, id));
        public Task<NestRecord?> GetDeletedMetadataAsync(Guid id, CancellationToken ct = default) => GetMetadataAsync(id, ct);
        public Task<NestPage> QueryDeletedAsync(NestQuery query, CancellationToken ct = default) => Task.FromResult(new NestPage { Items = [Record(5)], Total = 1, Limit = query.Limit });
        public Task<NestRevisionPage> GetRevisionsAsync(Guid id, int offset = 0, int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult(new NestRevisionPage { Items = [Record(5, id), Record(2, id)], Total = 2, Limit = limit });
        public Task<NestRevisionPage> GetDeletedRevisionsAsync(Guid id, int offset = 0, int limit = 100, CancellationToken cancellationToken = default) => GetRevisionsAsync(id, offset, limit, cancellationToken);
        public Task<IReadOnlyList<NestRecord>> ListAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<NestPage> QueryAsync(NestQuery query, CancellationToken ct = default) => Task.FromResult(new NestPage { Items = [Record(5), Record(5, SecondId)], Total = 2, Limit = query.Limit });
        public Task<byte[]?> GetFileAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<NestRecord> UploadAsync(byte[] file, NestRecord record, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<NestRecord> UpdateFileAsync(Guid id, byte[] file, NestRecord record, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<NestRecord> UpdateMetadataAsync(Guid id, NestRecord record, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) { Writes++; throw new NotImplementedException(); }
    }
}
