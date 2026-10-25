using System;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Data.MaterialLedger;

namespace OpenNest.Forms
{
    /// <summary>
    /// A nest document's MaterialLedger holds, read on demand and never polled. Applies only to
    /// nests saved to the nest server in Database mode while a ledger is configured; otherwise
    /// <see cref="Summary"/> stays null and nothing is shown. Refreshes run in the background
    /// and a newer one supersedes an older one, so neither the UI nor a save waits on the ledger.
    /// </summary>
    public sealed class NestHoldStatus : IDisposable
    {
        private readonly Document document;
        private readonly Func<bool> isDatabaseMode;
        private readonly Func<string> serverUrl;
        private readonly Func<MaterialLedgerSettings> ledgerSettings;
        private readonly Func<MaterialLedgerSettings, MaterialLedgerClient> createClient;
        private CancellationTokenSource current;
        private int savedSheets;

        public NestHoldStatus(Document document)
            : this(document, () => NestStorage.Settings.IsDatabaseMode, () => NestStorage.Settings.ServerUrl,
                () => MaterialLedgerStore.Settings, settings => MaterialLedgerStore.CreateClient(settings))
        {
        }

        internal NestHoldStatus(Document document, Func<bool> isDatabaseMode, Func<string> serverUrl,
            Func<MaterialLedgerSettings> ledgerSettings, Func<MaterialLedgerSettings, MaterialLedgerClient> createClient)
        {
            this.document = document ?? throw new ArgumentNullException(nameof(document));
            this.isDatabaseMode = isDatabaseMode;
            this.serverUrl = serverUrl;
            this.ledgerSettings = ledgerSettings;
            this.createClient = createClient;
        }

        /// <summary>Raised on the UI thread when <see cref="Summary"/> or <see cref="IsRefreshing"/> changes.</summary>
        public event EventHandler Changed;

        /// <summary>
        /// Raised after the nest is marked Has Been Cut while open holds remain; the argument is
        /// how many. Informational only: nothing waits on it.
        /// </summary>
        public event EventHandler<int> CloseoutNoteRequested;

        /// <summary>The last answer from the ledger; null when not applicable or not read yet.</summary>
        public LedgerHoldSummary Summary { get; private set; }

        public bool IsRefreshing => current != null;

        /// <summary>A saved server nest in Database mode with a ledger configured on this PC.</summary>
        public bool IsApplicable =>
            isDatabaseMode() && document.RemoteId != Guid.Empty && !document.IsFirstDatabaseSave(serverUrl())
            && ledgerSettings().IsConfigured;

        /// <summary>Whether a status line belongs on screen: applicable and read or being read.</summary>
        public bool IsVisible => IsApplicable && (Summary != null || IsRefreshing);

        /// <summary>The nest's page in MaterialLedger, where its holds are closed out.</summary>
        public string LedgerNestUrl => $"{ledgerSettings().BaseUrl.TrimEnd('/')}/Nests/Details/{document.RemoteId:D}";

        /// <summary>
        /// Rereads the holds after the nest was opened or saved, counting the sheets of the nest
        /// as it now is on the server.
        /// </summary>
        public Task RefreshAfterSaveAsync()
        {
            if (document.Nest != null)
                savedSheets = LedgerHoldSummary.PhysicalSheetCount(document.Nest);
            return RefreshAsync();
        }

        /// <summary>
        /// Rereads the holds against the sheet count of the last open or save. Never throws; a
        /// ledger failure becomes an unavailable summary.
        /// </summary>
        public async Task RefreshAsync()
        {
            current?.Cancel();
            if (!IsApplicable)
            {
                current = null;
                Summary = null;
                OnChanged();
                return;
            }

            using var cancel = new CancellationTokenSource();
            current = cancel;
            OnChanged();
            LedgerHoldSummary summary;
            try
            {
                var result = await createClient(ledgerSettings()).GetHoldsAsync(document.RemoteId, cancel.Token);
                summary = LedgerHoldSummary.From(result, savedSheets);
            }
            catch (OperationCanceledException)
            {
                return; // A newer refresh or Dispose took over.
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                summary = LedgerHoldSummary.From(new LedgerHoldsResult.Unreachable(ex.Message), savedSheets);
            }

            if (!ReferenceEquals(current, cancel))
                return;
            current = null;
            Summary = summary;
            OnChanged();
        }

        /// <summary>
        /// Called when the nest's status changes to Has Been Cut: rereads the holds and raises
        /// <see cref="CloseoutNoteRequested"/> when any are still open.
        /// </summary>
        public async Task NoteCutAsync()
        {
            if (!IsApplicable)
                return;
            await RefreshAsync();
            if (Summary is { State: not LedgerHoldState.Unavailable, OpenHolds: > 0 } summary)
                CloseoutNoteRequested?.Invoke(this, summary.OpenHolds);
        }

        public void Dispose()
        {
            current?.Cancel();
            current = null;
            Changed = null;
            CloseoutNoteRequested = null;
        }

        private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
