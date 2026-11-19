using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Data.MaterialLedger;
using OpenNest.Desktop;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Mcp.Database;

/// <summary>One sheet-size choice: a single size, or every size offered together as plate options.</summary>
public sealed class SheetChoice
{
    private SheetChoice(IReadOnlyList<Size> sizes, bool mixed)
    {
        Sizes = sizes;
        Mixed = mixed;
    }

    public IReadOnlyList<Size> Sizes { get; }

    /// <summary>True when the engine may mix the sizes (Nest.PlateOptions); false for one size alone.</summary>
    public bool Mixed { get; }

    public static SheetChoice Single(Size size) => new(new[] { size }, false);

    public static SheetChoice Together(IReadOnlyList<Size> sizes) => new(sizes, true);

    /// <summary>
    /// Sets the nest up to nest on this choice: one size as the plate default with no plate
    /// options, or every size as plate options (offered without limit).
    /// </summary>
    public void ApplyTo(Nest nest)
    {
        if (Mixed)
        {
            nest.PlateOptions = Sizes.Select(s => new PlateOption { Width = s.Width, Length = s.Length }).ToList();
            return;
        }
        nest.PlateDefaults.Size = Sizes[0];
        nest.PlateOptions = new List<PlateOption>();
    }

    public override string ToString() =>
        Mixed ? $"mixed ({string.Join(", ", Sizes.Select(SheetSizes.Format))})" : SheetSizes.Format(Sizes[0]);
}

/// <summary>What one sheet choice nested: kept or not, copies left, physical sheets and their area.</summary>
public sealed record SheetTrial(SheetChoice Choice, bool Kept, int Unplaced, int Sheets, double SheetArea,
    double Utilization, string Note)
{
    /// <summary>A kept layout first, then fewer copies unplaced, fewer sheets, less sheet area.</summary>
    public static int Compare(SheetTrial a, SheetTrial b)
    {
        var order = b.Kept.CompareTo(a.Kept);
        if (order == 0)
            order = a.Unplaced.CompareTo(b.Unplaced);
        if (order == 0)
            order = a.Sheets.CompareTo(b.Sheets);
        if (order == 0)
            order = a.SheetArea.CompareTo(b.SheetArea);
        return order;
    }

    public string Describe() => Kept
        ? string.Format(CultureInfo.InvariantCulture, "{0}: {1} sheet{2}, {3:0.##} sq, {4:P1} used, {5} unplaced",
            Name, Sheets, Sheets == 1 ? "" : "s", SheetArea, Utilization, Unplaced)
        : $"{Name}: no layout kept";

    /// <summary>The choice, or the nest's own plate defaults when no size was given.</summary>
    public string Name => Choice?.ToString() ?? "plate defaults";
}

/// <summary>The kept nest, the trial it came from and every trial run, best first.</summary>
public sealed record SheetTrialOutcome(bool Nested, string Note, SheetTrial Chosen, IReadOnlyList<SheetTrial> Trials);

/// <summary>Sheet sizes as tools take and print them: "72x144", width then length.</summary>
public static class SheetSizes
{
    public static string Format(Size size) =>
        string.Format(CultureInfo.InvariantCulture, "{0:0.###}x{1:0.###}", size.Width, size.Length);

    /// <summary>Parses "WxL" (x, X or *), each positive and finite; error says which one is not.</summary>
    public static IReadOnlyList<Size> Parse(IEnumerable<string> texts, out string error)
    {
        error = null;
        var sizes = new List<Size>();
        foreach (var text in texts ?? Array.Empty<string>())
        {
            var parts = (text ?? "").Split(new[] { 'x', 'X', '*' }, StringSplitOptions.TrimEntries);
            if (parts.Length != 2
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var length)
                || !double.IsFinite(width) || !double.IsFinite(length) || width <= 0 || length <= 0)
            {
                error = $"Sheet size '{text}' is not WIDTHxLENGTH, for example 60x120.";
                return null;
            }
            var size = new Size(width, length);
            if (!sizes.Any(s => s.Width == size.Width && s.Length == size.Length))
                sizes.Add(size);
        }
        return sizes;
    }
}

/// <summary>
/// Auto Nest of a nest no one is looking at, as Make Nests runs it (<see cref="ReleaseRenest"/>),
/// on the best of several sheet sizes: each size alone and, for two or more, all of them as
/// plate options, keeping the layout that leaves the fewest copies unplaced, then uses the
/// fewest sheets, then the least sheet area. Trials run on copies read back from the nest's
/// archive without MaterialLedger stock; the choice is then nested on the nest itself with the
/// ledger first, as Make Nests does.
/// </summary>
public static class SheetSizeTrials
{
    /// <summary>
    /// Nests <paramref name="nest"/> in place on the best choice from <paramref name="sizes"/>
    /// (empty: the nest's own plate defaults and options).
    /// </summary>
    public static async Task<SheetTrialOutcome> NestAsync(Nest nest, IReadOnlyList<Size> sizes,
        ReleaseLedgerStock ledger, string engineName, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(nest);
        var trials = new List<SheetTrial>();
        SheetChoice chosen = null;
        var choices = Choices(sizes);
        if (choices.Count > 1)
        {
            var archive = Write(nest);
            foreach (var choice in choices)
            {
                var copy = Read(archive);
                trials.Add(await RunAsync(copy, choice, null, engineName, token));
            }
            trials.Sort(SheetTrial.Compare);
            chosen = trials[0].Choice;
        }
        else if (choices.Count == 1)
        {
            chosen = choices[0];
        }

        var final = await RunAsync(nest, chosen, ledger, engineName, token);
        if (trials.Count == 0 && chosen != null)
            trials.Add(final);
        return new SheetTrialOutcome(final.Kept, final.Note, final, trials);
    }

    /// <summary>
    /// The trials on copies of <paramref name="archive"/> and the best copy nested, ledger
    /// first when given; the archive's nest itself is not changed.
    /// </summary>
    public static async Task<(Nest Nest, SheetTrialOutcome Outcome)> NestCopyAsync(byte[] archive,
        Action<Nest> prepare, IReadOnlyList<Size> sizes, ReleaseLedgerStock ledger, string engineName,
        CancellationToken token)
    {
        var choices = Choices(sizes);
        if (choices.Count == 0)
            choices = new List<SheetChoice> { null };
        var runs = new List<(Nest Nest, SheetTrial Trial)>();
        foreach (var choice in choices)
        {
            var copy = Read(archive);
            prepare?.Invoke(copy);
            runs.Add((copy, await RunAsync(copy, choice, choices.Count > 1 ? null : ledger, engineName, token)));
        }
        runs.Sort((a, b) => SheetTrial.Compare(a.Trial, b.Trial));
        var best = runs[0];
        if (choices.Count > 1 && ledger != null)
        {
            // The ledger's sheets are taken by the nest that keeps them, so only the choice runs on them.
            var copy = Read(archive);
            prepare?.Invoke(copy);
            best = (copy, await RunAsync(copy, best.Trial.Choice, ledger, engineName, token));
        }
        var trials = runs.Select(r => r.Trial).Where(t => t.Choice != null).ToList();
        return (best.Nest, new SheetTrialOutcome(best.Trial.Kept, best.Trial.Note, best.Trial, trials));
    }

    private static List<SheetChoice> Choices(IReadOnlyList<Size> sizes)
    {
        var choices = (sizes ?? Array.Empty<Size>()).Select(SheetChoice.Single).ToList();
        if (choices.Count > 1)
            choices.Add(SheetChoice.Together(sizes));
        return choices;
    }

    private static async Task<SheetTrial> RunAsync(Nest nest, SheetChoice choice, ReleaseLedgerStock ledger,
        string engineName, CancellationToken token)
    {
        choice?.ApplyTo(nest);
        var result = await Task.Run(() => ReleaseRenest.RunHeadlessAsync(nest, ledger,
            NestingEngineRegistry.Create(engineName), engineName, Nest.DefaultMinimumRemnantDimension, token), token);
        return Measure(nest, choice, result.Nested, result.Note);
    }

    /// <summary>The trial as the nest now stands.</summary>
    public static SheetTrial Measure(Nest nest, SheetChoice choice, bool kept, string note)
    {
        var plates = nest.Plates.ToList();
        var sheets = plates.Sum(p => p.Quantity);
        var area = plates.Sum(p => p.Area() * p.Quantity);
        var used = area > 0 ? plates.Sum(p => p.Utilization() * p.Area() * p.Quantity) / area : 0;
        var unplaced = nest.Drawings.Where(d => !d.IsCutOff).Sum(d => d.Quantity.Remaining);
        return new SheetTrial(choice, kept, unplaced, sheets, area, used, note ?? "");
    }

    public static byte[] Write(Nest nest)
    {
        using var stream = new MemoryStream();
        new NestWriter(nest) { BestFitStore = BestFitCacheStore.Shared }.Write(stream);
        return stream.ToArray();
    }

    public static Nest Read(byte[] archive)
    {
        using var stream = new MemoryStream(archive, writable: false);
        return new NestReader(stream) { BestFitStore = BestFitCacheStore.Shared }.Read();
    }
}
