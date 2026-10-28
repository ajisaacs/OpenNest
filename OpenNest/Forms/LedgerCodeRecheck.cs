using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data.MaterialLedger;

namespace OpenNest.Forms
{
    /// <summary>
    /// Before a nest with ledger item codes is saved to the nest server or posted, rereads the
    /// ledger's free stock and the codes on other open nests and asks about any code that is no
    /// longer this nest's to use. The answer counts for this attempt only; nothing is persisted.
    /// A ledger that is not set up or cannot be read skips the check: it never blocks a save.
    /// </summary>
    internal sealed class LedgerCodeRecheck
    {
        public enum Choice
        {
            Cancel,
            Proceed,
            RemoveCodes,
        }

        public const string RemoveLabel = "Remove Ledger Codes";

        private readonly Func<bool> ledgerConfigured;
        private readonly Func<string, string, double, CancellationToken, Task<LedgerStockResult>> fetchStock;
        private readonly Func<Guid, CancellationToken, Task<LedgerHoldsResult>> fetchHolds;
        private readonly Func<Guid, CancellationToken, Task<LedgerCodesInUse>> fetchCodesInUse;
        private readonly Func<IWin32Window, string, IReadOnlyList<LedgerCodeConflict>, Choice> ask;
        private readonly Action<string> showStatus;

        public LedgerCodeRecheck(Action<string> showStatus)
            : this(() => MaterialLedgerStore.Settings.IsConfigured,
                (material, grade, thickness, token) => MaterialLedgerStore.CreateClient().GetStockAsync(material, grade, thickness, token),
                (id, token) => MaterialLedgerStore.CreateClient().GetHoldsAsync(id, token),
                LedgerCodesOnOtherNests.ReadAsync,
                Ask,
                showStatus)
        {
        }

        internal LedgerCodeRecheck(Func<bool> ledgerConfigured,
            Func<string, string, double, CancellationToken, Task<LedgerStockResult>> fetchStock,
            Func<Guid, CancellationToken, Task<LedgerHoldsResult>> fetchHolds,
            Func<Guid, CancellationToken, Task<LedgerCodesInUse>> fetchCodesInUse,
            Func<IWin32Window, string, IReadOnlyList<LedgerCodeConflict>, Choice> ask,
            Action<string> showStatus)
        {
            this.ledgerConfigured = ledgerConfigured;
            this.fetchStock = fetchStock;
            this.fetchHolds = fetchHolds;
            this.fetchCodesInUse = fetchCodesInUse;
            this.ask = ask;
            this.showStatus = showStatus ?? (_ => { });
        }

        /// <summary>
        /// True when <paramref name="action"/> ("Save" or "Post") may go on: no codes, nothing
        /// to check, nothing taken, or the programmer chose to go on or to remove the codes.
        /// False when they cancelled or the window closed meanwhile. A Save As
        /// (<paramref name="asCopy"/>) is a new nest: the original's codes and holds count as
        /// another nest's.
        /// </summary>
        public async Task<bool> ConfirmAsync(IWin32Window owner, EditNestForm form, string action, bool asCopy = false)
        {
            var nest = form.Nest;
            if (!LedgerCodeCheck.HasCodes(nest.Plates) || !ledgerConfigured())
                return true;
            var material = nest.Material?.Name?.Trim() ?? "";
            if (material.Length == 0 || !(nest.Thickness > 0))
                return true;

            var remoteId = asCopy ? Guid.Empty : form.Document.RemoteId;
            LedgerStockResult stock;
            LedgerHoldsResult holds;
            LedgerCodesInUse inUse;
            try
            {
                var stockTask = fetchStock(material, nest.Material.Grade?.Trim() ?? "",
                    LedgerStockMapper.ThicknessInches(nest.Thickness, nest.Units), CancellationToken.None);
                var holdsTask = remoteId == Guid.Empty
                    ? Task.FromResult<LedgerHoldsResult>(new LedgerHoldsResult.Ok(Array.Empty<LedgerHold>()))
                    : fetchHolds(remoteId, CancellationToken.None);
                var inUseTask = fetchCodesInUse(remoteId, CancellationToken.None);
                await Task.WhenAll(stockTask, holdsTask, inUseTask);
                (stock, holds, inUse) = (stockTask.Result, holdsTask.Result, inUseTask.Result);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                showStatus($"Ledger codes not re-checked: {ex.Message}");
                return !form.IsDisposed;
            }
            if (form.IsDisposed)
                return false;

            // The nest's own holds tell its sheets from ones taken by other work; without them
            // every held sheet would look taken.
            if (stock is not LedgerStockResult.Ok ok || holds is not LedgerHoldsResult.Ok held)
            {
                if ((Unavailable(stock) ?? Unavailable(holds)) is { } reason)
                    showStatus($"Ledger codes not re-checked: {reason}");
                return true;
            }

            var conflicts = LedgerCodeCheck.Find(nest.Plates, ok.Items, held.Holds, inUse);
            if (conflicts.Count == 0)
            {
                if (!inUse.IsKnown)
                    showStatus($"Ledger codes checked against MaterialLedger only; other nests not checked: {inUse.Unavailable}");
                return true;
            }

            switch (ask(owner, action, conflicts))
            {
                case Choice.Proceed:
                    return !form.IsDisposed;
                case Choice.RemoveCodes when !form.IsDisposed:
                    var codes = conflicts.Select(c => c.ItemCode).Distinct(StringComparer.Ordinal).ToList();
                    var removed = 0;
                    form.RecordChange(RemoveLabel, () => removed = LedgerCodeCheck.Remove(form.Nest.Plates, codes));
                    form.PlateView.Invalidate();
                    showStatus($"Removed {codes.Count} ledger item code{(codes.Count == 1 ? "" : "s")} from {removed} sheet{(removed == 1 ? "" : "s")}");
                    return true;
                default:
                    showStatus($"{action} cancelled");
                    return false;
            }
        }

        private static string Unavailable(LedgerStockResult result) => result switch
        {
            LedgerStockResult.Ok => null,
            LedgerStockResult.NotConfigured => null,
            LedgerStockResult.Unmapped => "MaterialLedger has no mapping for this material",
            LedgerStockResult.Unauthorized => "MaterialLedger rejected the API key",
            LedgerStockResult.Unreachable unreachable => $"MaterialLedger unreachable ({unreachable.Reason})",
            _ => "MaterialLedger did not answer",
        };

        private static string Unavailable(LedgerHoldsResult result) => result switch
        {
            LedgerHoldsResult.Ok => null,
            LedgerHoldsResult.NotConfigured => null,
            LedgerHoldsResult.Unauthorized => "MaterialLedger rejected the API key",
            LedgerHoldsResult.Unreachable unreachable => $"MaterialLedger unreachable ({unreachable.Reason})",
            _ => "MaterialLedger did not answer",
        };

        private static Choice Ask(IWin32Window owner, string action, IReadOnlyList<LedgerCodeConflict> conflicts)
        {
            // A fresh dialog on every attempt: consent never outlives this call.
            using var dialog = new LedgerCodeConflictForm(action, conflicts);
            dialog.ShowDialog(owner);
            return dialog.Choice;
        }
    }

    /// <summary>Reads the ledger codes on other open server nests, or why they could not be read.</summary>
    internal static class LedgerCodesOnOtherNests
    {
        /// <summary>
        /// The codes on every open server nest except <paramref name="excludeNestId"/>. File mode
        /// and an unusable server URL come back unknown, never as an exception.
        /// </summary>
        public static Task<LedgerCodesInUse> ReadAsync(Guid excludeNestId, CancellationToken cancellationToken)
        {
            if (!NestStorage.Settings.IsDatabaseMode)
                return LedgerCodesInUse.ReadAsync(null, excludeNestId, cancellationToken);
            Data.INestRepository repository;
            try
            {
                repository = NestStorage.CreateRepository();
            }
            catch (ArgumentException)
            {
                return Task.FromResult(LedgerCodesInUse.Unknown("the nest server address is not valid"));
            }
            return LedgerCodesInUse.ReadAsync(repository, excludeNestId, cancellationToken);
        }
    }
}
