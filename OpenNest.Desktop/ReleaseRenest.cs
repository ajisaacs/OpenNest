using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Data.MaterialLedger;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;

namespace OpenNest.Desktop
{
    /// <summary>
    /// What Make Nests' renest step kept, its note and the ledger codes' locations.
    /// </summary>
    public sealed record ReleaseRenestResult(bool Nested, string Note,
        IReadOnlyDictionary<string, string> LedgerLocations);

    /// <summary>
    /// Make Nests' Auto Nest of one released nest: on the run's MaterialLedger stock first,
    /// then on the nest's plate defaults (<see cref="NestRenest.RunStagedAsync"/>), with the
    /// ledger codes given to the plates inside each commit, so they are part of its undo step.
    /// Shared by the headless and the live (main window) release.
    /// </summary>
    public static class ReleaseRenest
    {
        /// <summary>
        /// Attaches a kept result's sheets to the nest's plates and returns them. It must call
        /// <c>finish</c> with those plates before its undo step closes; that gives them their
        /// ledger codes.
        /// </summary>
        public delegate IReadOnlyList<Plate> Commit(NestPipelineResult result,
            Action<IReadOnlyList<Plate>> finish);

        /// <summary>
        /// Nests <paramref name="nest"/>, which has no parts placed, on <paramref name="ledger"/>'s
        /// stock (null: the plate defaults only) and then its plate defaults. The ledger is not
        /// read when nothing has a quantity to nest.
        /// </summary>
        public static async Task<ReleaseRenestResult> RunAsync(Nest nest, ReleaseLedgerStock ledger,
            string engineName, double minimumSalvageDimension,
            Func<NestPipelineRequest, Task<NestPipelineResult>> solve, Commit commit,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(nest);
            ArgumentNullException.ThrowIfNull(solve);
            ArgumentNullException.ThrowIfNull(commit);
            var wanted = nest.Drawings.Any(d => !d.IsCutOff && d.Quantity.Remaining > 0);
            var offer = ledger != null && wanted ? await ledger.OfferAsync(nest, token) : null;
            var locations = new Dictionary<string, string>(StringComparer.Ordinal);

            var renested = await NestRenest.RunStagedAsync(nest, engineName, offer?.Options,
                minimumSalvageDimension, solve,
                result => commit(result, plates =>
                {
                    if (offer == null)
                        return;
                    var stock = NestPipelineCommit.PairWithStock(result, plates)
                        .Select(p => (p.Plate, p.Stock.Id))
                        .ToList();
                    foreach (var (code, location) in ledger.AssignCodes(nest, offer, stock))
                        locations[code] = location;
                }),
                token);

            var ledgerNote = offer == null
                ? null
                : ReleaseLedgerStock.Note(offer, nest, renested.FirstStockNotKept);
            return new ReleaseRenestResult(renested.Nested, Join(renested.Note, ledgerNote),
                locations);
        }

        /// <summary>
        /// <see cref="RunAsync"/> for a nest no one is looking at: every plate is removed, the
        /// layouts are solved on the calling thread and attached as new plates.
        /// </summary>
        public static Task<ReleaseRenestResult> RunHeadlessAsync(Nest nest,
            ReleaseLedgerStock ledger, INestingEngine engine, string engineName,
            double minimumSalvageDimension,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(nest);
            ArgumentNullException.ThrowIfNull(engine);
            token.ThrowIfCancellationRequested();
            nest.Plates.Clear();
            nest.UpdateDrawingQuantities();
            return RunAsync(nest, ledger, engineName, minimumSalvageDimension,
                request => Task.FromResult(
                    NestPipeline.Run(engine, engineName, request, null, token)),
                (result, finish) =>
                {
                    var plates = NestPipelineCommit.ApplyToPlates(result, nest.CreatePlate,
                        token: token);
                    finish(plates);
                    return plates;
                },
                token);
        }

        /// <summary>Auto Nest's note, then what MaterialLedger stock the nest used.</summary>
        public static string Join(string renestNote, string ledgerNote) =>
            string.Join(" ", new[] { renestNote, ledgerNote }.Where(n => !string.IsNullOrEmpty(n)));
    }
}
