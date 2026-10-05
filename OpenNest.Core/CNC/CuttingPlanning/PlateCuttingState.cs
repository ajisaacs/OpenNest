using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Collections;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>
/// Exact caller-thread record of everything a cutting proposal for one plate depends on:
/// part list instance and order, plate quantity/size/quadrant/settings, cutoff definitions, and
/// each part's complete cutting state with owned copies of its placed and drawing programs,
/// drawing cutoff classification and exact settings content.
/// A commit compares it with the live plate and refuses when anything differs.
/// </summary>
public sealed class PlateCuttingState
{
    private readonly ObservableList<Part> partList;
    private readonly PartRecord[] parts;
    private readonly int quantity;
    private readonly Size size;
    private readonly int quadrant;
    private readonly ObservableList<CutOff> cutOffList;
    private readonly CutOffRecord[] cutOffs;
    private readonly Dictionary<Drawing, (Program Program, Program Copy, bool IsCutOff)> drawings;
    private readonly Dictionary<CuttingParameters, string> settings;
    private readonly CuttingParameters plateSettings;

    private PlateCuttingState(Plate plate, PartRecord[] parts, CutOffRecord[] cutOffs,
        Dictionary<Drawing, (Program, Program, bool)> drawings, Dictionary<CuttingParameters, string> settings)
    {
        this.settings = settings;
        plateSettings = plate.CuttingParameters;
        Plate = plate;
        partList = plate.Parts;
        this.parts = parts;
        quantity = plate.Quantity;
        size = plate.Size;
        quadrant = plate.Quadrant;
        cutOffList = plate.CutOffs;
        this.cutOffs = cutOffs;
        this.drawings = drawings;
        Order = Array.AsReadOnly(parts.Select(p => p.Part).ToArray());
    }

    public Plate Plate { get; }

    /// <summary>Captured part order (reference identity).</summary>
    public IReadOnlyList<Part> Order { get; }

    /// <summary>
    /// Captures on the caller thread. Unsupported or malformed programs throw
    /// <see cref="ArgumentException"/> or <see cref="NotSupportedException"/>. Settings whose
    /// authored state cannot be captured exactly are recorded as refused: the plate stays
    /// plannable but every later commit against it reports <c>Stale</c>.
    /// </summary>
    public static PlateCuttingState Capture(Plate plate, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(plate);
        if (plate.Parts == null || plate.CutOffs == null)
            throw new ArgumentException("Plate part and cutoff lists are required.");
        var drawings = new Dictionary<Drawing, (Program, Program, bool)>(ReferenceEqualityComparer.Instance);
        var settings = new Dictionary<CuttingParameters, string>(ReferenceEqualityComparer.Instance);
        Fingerprint(plate.CuttingParameters);
        var records = new List<PartRecord>(plate.Parts.Count);
        foreach (var part in plate.Parts)
        {
            token.ThrowIfCancellationRequested();
            if (part?.BaseDrawing == null || part.Program == null)
                throw new ArgumentException("Plate contains a missing part, drawing or program.");
            var drawing = part.BaseDrawing;
            if (!drawings.ContainsKey(drawing))
                drawings.Add(drawing, (drawing.Program, drawing.Program == null ? null
                    : OwnedProgramCopy.Copy(drawing.Program, token), drawing.IsCutOff));
            var state = part.CaptureCuttingState();
            Fingerprint(state.CuttingParameters);
            records.Add(new(part, state, OwnedProgramCopy.Copy(part.Program, token), BoxValues(state.BoundingBox)));
        }
        var cutOffs = plate.CutOffs.Select(c => c == null
            ? throw new ArgumentException("Plate contains a missing cutoff definition.")
            : new CutOffRecord(c, c.Drawing, c.Axis, c.Position, c.StartLimit, c.EndLimit)).ToArray();
        return new(plate, records.ToArray(), cutOffs, drawings, settings);

        void Fingerprint(CuttingParameters parameters)
        {
            // A refused capture is stored as-is; Difference treats it as never current, so
            // every Apply for such settings is Stale and no foreign code ever runs at Apply.
            if (parameters != null && !settings.ContainsKey(parameters))
                settings.Add(parameters, StateFingerprint.Of(parameters));
        }
    }

    /// <summary>True when the live plate still has exactly the captured state.</summary>
    public bool IsCurrent(CancellationToken token = default) => Difference(token) == null;

    /// <summary>Null when current; otherwise the first observed difference.</summary>
    public string Difference(CancellationToken token = default)
    {
        const string Current = "current";
        const string Stale = "stale";
        var checkedSettings = new Dictionary<CuttingParameters, string>(ReferenceEqualityComparer.Instance);
        var plate = Plate;
        if (!ReferenceEquals(plate.Parts, partList) || !ReferenceEquals(plate.CutOffs, cutOffList))
            return "The plate's part or cutoff list was replaced.";
        if (plate.Quantity != quantity || !Bits(plate.Size.Width, size.Width)
            || !Bits(plate.Size.Length, size.Length) || plate.Quadrant != quadrant)
            return "Plate quantity, size or quadrant changed.";
        if (!ReferenceEquals(plate.CuttingParameters, plateSettings) || !SameSettings(plateSettings))
            return "Plate cutting settings changed.";
        if (plate.Parts.Count != parts.Length)
            return "Parts were added or removed.";
        for (var i = 0; i < parts.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var record = parts[i];
            var part = plate.Parts[i];
            if (!ReferenceEquals(part, record.Part))
                return $"Part order changed at position {i + 1}.";
            var live = part.CaptureCuttingState();
            var was = record.State;
            if (!ReferenceEquals(live.Program, was.Program) || live.OwnsProgram != was.OwnsProgram
                || !ReferenceEquals(live.CuttingParameters, was.CuttingParameters)
                || !ReferenceEquals(live.BoundingBox, was.BoundingBox))
                return $"Part {i + 1} program, settings or bounds were replaced.";
            if (!Bits(live.Location.X, was.Location.X) || !Bits(live.Location.Y, was.Location.Y)
                || !Bits(live.PreLeadInRotation, was.PreLeadInRotation)
                || live.HasManualLeadIns != was.HasManualLeadIns || live.LeadInsLocked != was.LeadInsLocked)
                return $"Part {i + 1} pose, lead-in or lock state changed.";
            if (!BoxValues(live.BoundingBox).SequenceEqual(record.Bounds))
                return $"Part {i + 1} bounds changed.";
            if (!ProgramContent.Equal(part.Program, record.Program, token))
                return $"Part {i + 1} program was edited in place.";
            if (!SameSettings(live.CuttingParameters))
                return $"Part {i + 1} cutting settings were edited in place.";
            var (drawingProgram, drawingCopy, isCutOff) = drawings[part.BaseDrawing];
            if (!ReferenceEquals(part.BaseDrawing.Program, drawingProgram)
                || !ProgramContent.Equal(drawingProgram, drawingCopy, token))
                return $"Part {i + 1} drawing program changed.";
            if (part.BaseDrawing.IsCutOff != isCutOff)
                return $"Part {i + 1} cutoff classification changed.";
        }
        if (plate.CutOffs.Count != cutOffs.Length)
            return "Cutoffs were added or removed.";
        for (var i = 0; i < cutOffs.Length; i++)
        {
            var record = cutOffs[i];
            var cutOff = plate.CutOffs[i];
            if (!ReferenceEquals(cutOff, record.CutOff) || !ReferenceEquals(cutOff.Drawing, record.Drawing)
                || cutOff.Axis != record.Axis || !Bits(cutOff.Position.X, record.Position.X)
                || !Bits(cutOff.Position.Y, record.Position.Y)
                || !Bits(cutOff.StartLimit, record.StartLimit) || !Bits(cutOff.EndLimit, record.EndLimit))
                return $"Cutoff {i + 1} definition changed.";
        }
        // Part.Rotation derives from the manual flag, PreLeadInRotation and Program.Rotation,
        // all compared exactly above.
        return null;

        // References were compared already; this catches in-place edits of the same object.
        // Each distinct settings object is fingerprinted at most once per check. A refused
        // capture (Invalid) is never current: the state cannot be proven unchanged, and the
        // live object is deliberately not re-read. A fingerprint that fails on re-read is
        // likewise stale, never an escaping exception (cancellation stays distinct).
        bool SameSettings(CuttingParameters parameters)
        {
            if (parameters == null)
                return true;
            if (checkedSettings.TryGetValue(parameters, out var decision))
                return decision == Current;
            var current = false;
            if (settings.TryGetValue(parameters, out var captured)
                && captured != StateFingerprint.Invalid)
            {
                try
                {
                    current = captured == StateFingerprint.Of(parameters);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    current = false;
                }
            }
            checkedSettings[parameters] = current ? Current : Stale;
            return current;
        }
    }

    private static long[] BoxValues(Box box) => box == null ? [] :
        [BitConverter.DoubleToInt64Bits(box.X), BitConverter.DoubleToInt64Bits(box.Y),
         BitConverter.DoubleToInt64Bits(box.Width), BitConverter.DoubleToInt64Bits(box.Length)];

    private static bool Bits(double a, double b) =>
        BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

    private static bool Bits(double? a, double? b) => a.HasValue == b.HasValue
        && (!a.HasValue || Bits(a.Value, b.Value));

    private sealed record PartRecord(Part Part, PartCuttingState State, Program Program, long[] Bounds);

    private sealed record CutOffRecord(CutOff CutOff, Drawing Drawing, CutOffAxis Axis, Vector Position,
        double? StartLimit, double? EndLimit);
}
