using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNest.CommonLines;

namespace OpenNest.Engine.Jobs.Remote;

/// <summary>
/// The parts of a <see cref="NestPipelineRequest"/> that are not drawings: the engine, each
/// item's quantity, priority and rotation with the index of its drawing, the stock and the job
/// options. Drawings travel beside it in a <c>.nest</c> archive (<c>OpenNest.Api.NestJobPackage</c>).
/// A remote solve never runs with <see cref="NestJobOptions.SearchUntilCancelled"/>: it has no
/// time limit, so a slow server finds the same nest a fast one does, later.
/// </summary>
public sealed class NestJobDocument
{
    /// <summary>
    /// Version 2 nests higher priority numbers first. Version 1 jobs, from desktops that nested
    /// lower numbers first, still read, with their priorities converted
    /// (<see cref="LegacyPriority"/>); an older worker refuses version 2 instead of reversing it.
    /// </summary>
    public const int CurrentVersion = 2;

    private const int LowerFirstPriorityVersion = 1;

    /// <summary>
    /// A version 2 job that also uses common lines (<see cref="OptionsJson.CommonLineUsage"/> on
    /// with a positive kerf). Written only then, so a worker that cannot make common lines refuses
    /// the job rather than solving it without them, which the desktop's replay would reject.
    /// </summary>
    public const int CommonLineVersion = 3;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public int Version { get; set; } = CurrentVersion;
    public string EngineName { get; set; }

    /// <summary>Each drawing's name in list order, so a reader can tell the list arrived in that order.</summary>
    public List<string> DrawingNames { get; set; } = new();

    /// <summary>
    /// Each drawing's <see cref="PartGeometrySnapshot.Fingerprint"/> as sent, in list order (empty
    /// for a drawing without supported geometry). A desktop reopening the job in a later session
    /// checks its drawings against these (<see cref="RemoteNestSubmission.Reopen"/>). Null from
    /// clients that predate it; workers ignore it.
    /// </summary>
    public List<string> DrawingFingerprints { get; set; }

    public List<ItemJson> Items { get; set; } = new();
    public List<NestStockJson> Stock { get; set; } = new();
    public OptionsJson JobOptions { get; set; }

    /// <summary>
    /// Describes <paramref name="request"/>, naming each item's drawing by its position in
    /// <paramref name="drawings"/>, which must hold every item's drawing.
    /// </summary>
    public static NestJobDocument From(NestPipelineRequest request, IReadOnlyList<Drawing> drawings)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(drawings);
        var document = new NestJobDocument
        {
            EngineName = request.EngineName,
            DrawingNames = drawings.Select(d => d?.Name ?? "").ToList(),
            DrawingFingerprints = drawings.Select(RemoteNestSubmission.Fingerprint).ToList(),
            Stock = request.Stock.Select(NestStockJson.From).ToList(),
            JobOptions = request.Options is { } o ? new OptionsJson
            {
                PlacementStrategy = o.PlacementStrategy,
                MaxPlates = o.MaxPlates,
                SalvageRate = o.SalvageRate,
                MinimumSalvageDimension = o.MinimumSalvageDimension,
                OffcutCut = o.OffcutCut,
                CommonLineUsage = o.CommonLineUsage,
                CommonLineKerf = o.CommonLineKerf,
            } : null,
        };
        if (request.Options?.UsesCommonLines == true)
            document.Version = CommonLineVersion;
        foreach (var item in request.Items)
        {
            var index = item?.Drawing == null ? -1 : IndexOf(drawings, item.Drawing);
            if (item != null && index < 0)
                throw new ArgumentException("Every item's drawing must be in the drawing list.", nameof(drawings));
            document.Items.Add(item == null ? null : new ItemJson
            {
                Drawing = index,
                Quantity = item.Quantity,
                Priority = item.Priority,
                StepAngle = item.StepAngle,
                RotationStart = item.RotationStart,
                RotationEnd = item.RotationEnd,
            });
        }
        return document;
    }

    /// <summary>The request this describes, with drawings taken from <paramref name="drawings"/> by position.</summary>
    public NestPipelineRequest ToRequest(IReadOnlyList<Drawing> drawings)
    {
        ArgumentNullException.ThrowIfNull(drawings);
        if (Version != CurrentVersion && Version != LowerFirstPriorityVersion && Version != CommonLineVersion)
            throw new FormatException($"Nest job version {Version} is not supported; this build reads {CommonLineVersion}.");
        if (string.IsNullOrWhiteSpace(EngineName))
            throw new FormatException("The nest job names no engine.");
        if (DrawingNames == null || DrawingNames.Count != drawings.Count
            || DrawingNames.Where((name, i) => !string.Equals(name, drawings[i]?.Name ?? "", StringComparison.Ordinal)).Any())
            throw new FormatException("The nest job's drawings do not match the drawings sent with it.");
        var items = new List<NestItem>(Items?.Count ?? 0);
        foreach (var item in Items ?? new())
        {
            if (item == null)
            {
                items.Add(null);
                continue;
            }
            if (item.Drawing < 0 || item.Drawing >= drawings.Count)
                throw new FormatException($"A nest job item names drawing {item.Drawing}, which the job does not hold.");
            items.Add(new NestItem
            {
                Drawing = drawings[item.Drawing],
                Quantity = item.Quantity,
                Priority = Version == LowerFirstPriorityVersion ? LegacyPriority.Convert(item.Priority) : item.Priority,
                StepAngle = item.StepAngle,
                RotationStart = item.RotationStart,
                RotationEnd = item.RotationEnd,
            });
        }
        var options = JobOptions is { } o
            ? new NestJobOptions(o.PlacementStrategy ?? "Lattice", o.MaxPlates, o.SalvageRate, o.MinimumSalvageDimension,
                searchUntilCancelled: false, o.OffcutCut)
            {
                CommonLineUsage = Enum.IsDefined(o.CommonLineUsage) ? o.CommonLineUsage
                    : throw new FormatException("The nest job's common-line usage is not one this build knows."),
                CommonLineKerf = double.IsFinite(o.CommonLineKerf) && o.CommonLineKerf >= 0 ? o.CommonLineKerf
                    : throw new FormatException("The nest job's common-line kerf is not a finite, nonnegative number."),
            }
            : null;
        return new NestPipelineRequest(EngineName, items,
            (Stock ?? new()).Select(s => s?.ToStock() ?? throw new FormatException("A nest job stock entry is empty.")).ToList(),
            options);
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static NestJobDocument FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<NestJobDocument>(json, Options)
                ?? throw new FormatException("The nest job is empty.");
        }
        catch (JsonException ex) { throw new FormatException("The nest job is not valid JSON.", ex); }
    }

    private static int IndexOf(IReadOnlyList<Drawing> drawings, Drawing drawing)
    {
        for (var i = 0; i < drawings.Count; i++)
        {
            if (ReferenceEquals(drawings[i], drawing))
                return i;
        }
        return -1;
    }

    public sealed class ItemJson
    {
        public int Drawing { get; set; }
        public int Quantity { get; set; }
        public int Priority { get; set; }
        public double StepAngle { get; set; }
        public double RotationStart { get; set; }
        public double RotationEnd { get; set; }
    }

    public sealed class OptionsJson
    {
        public string PlacementStrategy { get; set; }
        public int? MaxPlates { get; set; }
        public double SalvageRate { get; set; }
        public double MinimumSalvageDimension { get; set; }
        public CutOffAxis? OffcutCut { get; set; }

        /// <summary><see cref="NestJobOptions.CommonLineUsage"/>; absent (off) before version 3.</summary>
        public CommonLineUsage CommonLineUsage { get; set; }

        /// <summary><see cref="NestJobOptions.CommonLineKerf"/>; absent (zero) before version 3.</summary>
        public double CommonLineKerf { get; set; }
    }
}
