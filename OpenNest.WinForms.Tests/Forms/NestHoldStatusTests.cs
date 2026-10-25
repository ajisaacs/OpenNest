using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows.Forms;
using OpenNest.Data.MaterialLedger;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

public class NestHoldStatusTests
{
    private const string ServerUrl = "https://nest-storage.example.test";
    private static readonly MaterialLedgerSettings Ledger = new() { BaseUrl = "http://ledger:5000", ApiKey = "k" };

    [Fact]
    public async Task FileModeNestsShowNoHoldsAndNeverAskTheLedger()
    {
        var requests = 0;
        var status = Status(Bound(), databaseMode: false, _ => { requests++; return Holds("Approved"); });

        await status.RefreshAfterSaveAsync();
        await status.NoteCutAsync();

        Assert.False(status.IsApplicable);
        Assert.False(status.IsVisible);
        Assert.Null(status.Summary);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task UnsavedNestsAndUnconfiguredLedgersShowNothing()
    {
        var requests = 0;
        var unsaved = Status(new Document { Nest = Nest(1) }, databaseMode: true, _ => { requests++; return Holds(); });
        await unsaved.RefreshAfterSaveAsync();
        Assert.False(unsaved.IsVisible);

        var noLedger = Status(Bound(), databaseMode: true, _ => { requests++; return Holds(); }, new MaterialLedgerSettings());
        await noLedger.RefreshAfterSaveAsync();
        Assert.False(noLedger.IsVisible);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task ServerNestsSummarizeHoldsAgainstSavedSheets()
    {
        var document = Bound(sheets: 4);
        var changes = 0;
        HttpRequestMessage seen = null;
        var status = Status(document, databaseMode: true, request => { seen = request; return Holds("Approved", "Proposed", "Released"); });
        status.Changed += (_, _) => changes++;

        await status.RefreshAfterSaveAsync();

        Assert.True(status.IsVisible);
        Assert.False(status.IsRefreshing);
        Assert.Equal("Ledger: 2 of 4 held, 2 short", status.Summary.Text);
        Assert.Contains(document.RemoteId.ToString("D"), seen.RequestUri!.Query);
        Assert.Equal($"http://ledger:5000/Nests/Details/{document.RemoteId:D}", status.LedgerNestUrl);
        Assert.Equal(2, changes); // Refreshing, then the answer.

        // An on-demand refresh counts the sheets of the last save, not unsaved edits.
        document.Nest.Plates[0].Quantity = 10;
        await status.RefreshAsync();
        Assert.Equal(4, status.Summary.Sheets);
    }

    [Fact]
    public async Task UnreachableLedgerIsShownAsUnavailable()
    {
        var status = Status(Bound(), databaseMode: true, _ => throw new HttpRequestException("down"));
        await status.RefreshAfterSaveAsync();
        Assert.Equal("Ledger: unavailable", status.Summary.Text);
    }

    [Fact]
    public async Task CutNoteAppearsOnlyWithOpenHolds()
    {
        var open = Status(Bound(), databaseMode: true, _ => Holds("Approved", "AwaitingCloseout", "ClosedConsumed"));
        var noted = new List<int>();
        open.CloseoutNoteRequested += (_, count) => noted.Add(count);
        await open.NoteCutAsync();
        Assert.Equal(new[] { 2 }, noted);

        foreach (var reply in new Func<HttpRequestMessage, HttpResponseMessage>[]
        {
            _ => Holds(),
            _ => Holds("ClosedConsumed", "Cancelled"),
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
        })
        {
            var closed = Status(Bound(), databaseMode: true, reply);
            closed.CloseoutNoteRequested += (_, count) => noted.Add(count);
            await closed.NoteCutAsync();
        }
        Assert.Equal(new[] { 2 }, noted);
    }

    [Fact]
    public void CloseoutNoteLinksToTheLedgerNest() => RunSta(() =>
    {
        using var note = new LedgerCloseoutNote("N-1", 4, "http://ledger:5000/Nests/abc");
        Assert.Contains("Close out 4 holds in MaterialLedger", note.Message.Text);
        Assert.Equal("http://ledger:5000/Nests/abc", note.Link.Tag);
        Assert.Contains("Close out 1 hold in", LedgerCloseoutNote.MessageText("N-1", 1));
    });

    [Fact]
    public void StatusBarHidesTheLedgerLabelForFileModeNests() => RunSta(() =>
    {
        using var form = new EditNestForm(Nest(2));
        var ledger = MainStatusBar.CreateLedgerLabel();
        ledger.Visible = true;
        ledger.Text = "stale";
        var bar = new MainStatusBar(new ToolStripStatusLabel(), new ToolStripStatusLabel(), new ToolStripStatusLabel(),
            new ToolStripStatusLabel(), new ToolStripStatusLabel(), new ToolStripStatusLabel(), ledger);

        bar.Bind(form);

        Assert.False(form.HoldStatus.IsVisible); // Never saved to the server.
        Assert.False(ledger.Visible);
        Assert.Equal("", ledger.Text);
        bar.Unbind();
    });

    private static NestHoldStatus Status(Document document, bool databaseMode,
        Func<HttpRequestMessage, HttpResponseMessage> reply, MaterialLedgerSettings settings = null) =>
        new(document, () => databaseMode, () => ServerUrl, () => settings ?? Ledger,
            s => new MaterialLedgerClient(new HttpClient(new Handler(reply)), s));

    private static Document Bound(int sheets = 1)
    {
        var document = new Document { Nest = Nest(sheets) };
        document.BindRemote(Guid.NewGuid(), ServerUrl, 3);
        return document;
    }

    private static Nest Nest(int sheets)
    {
        var nest = new Nest("N-1");
        var plate = new Plate(48, 96) { Quantity = sheets };
        plate.Parts.Add(new Part(Square()));
        nest.Plates.Add(plate);
        return nest;
    }

    private static Drawing Square()
    {
        var program = new CNC.Program();
        program.Codes.Add(new CNC.RapidMove(new Geometry.Vector(0, 0)));
        program.Codes.Add(new CNC.LinearMove(new Geometry.Vector(4, 0)));
        program.Codes.Add(new CNC.LinearMove(new Geometry.Vector(4, 4)));
        program.Codes.Add(new CNC.LinearMove(new Geometry.Vector(0, 0)));
        return new Drawing("square", program);
    }

    private static HttpResponseMessage Holds(params string[] statuses)
    {
        var holds = string.Join(",", statuses.Select((status, i) =>
            $$"""{ "materialReservationId": {{i + 1}}, "itemCode": "S-{{i}}", "widthIn": 48, "lengthIn": 96, "status": "{{status}}", "locationCode": "A1", "createdAtUtc": "2026-10-20T14:00:00Z" }"""));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{ "openNestNestId": "00000000-0000-0000-0000-000000000000", "holds": [{{holds}}] }""",
                Encoding.UTF8, "application/json"),
        };
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(reply(request));
    }

    private static void RunSta(Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(15), "The STA test did not complete.");
}
