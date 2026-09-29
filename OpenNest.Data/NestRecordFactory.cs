using System.Linq;

namespace OpenNest.Data;

/// <summary>
/// Builds the shareable <see cref="NestRecord"/> metadata from a live <see cref="Nest"/>.
/// Counts are computed here (not carried on <see cref="Nest"/> itself) so every caller
/// that saves to the nest server gets the same definition of "plate" and "part" counts.
/// </summary>
public static class NestRecordFactory
{
    /// <summary>
    /// Builds a record for uploading <paramref name="nest"/>. <paramref name="id"/> is the
    /// client-tracked id: <see cref="System.Guid.Empty"/> for a first save (the server
    /// assigns one), or the previously returned id to update an existing record.
    /// <paramref name="fileSize"/> is the size of the serialized .nest archive being
    /// uploaded alongside this record; the server also recomputes it independently.
    /// </summary>
    public static NestRecord FromNest(Nest nest, System.Guid id, long fileSize)
    {
        ArgumentNullException.ThrowIfNull(nest);

        return new NestRecord
        {
            Id = id,
            Name = nest.Name ?? "",
            Customer = nest.Customer ?? "",
            DateCreated = nest.DateCreated,
            DateModified = nest.DateLastModified,
            Material = nest.Material?.Name ?? "",
            Thickness = nest.Thickness,
            Status = nest.Status,
            PlateCount = nest.Plates.Count,
            PartCount = nest.Plates.Sum(CountNonCutoffParts),
            // Nest has no separate "comments" field; Notes is the closest analog
            // shown in the nest info dialog, so it round-trips as Comments here.
            Comments = nest.Notes ?? "",
            MadeBy = nest.MadeBy ?? "",
            FileSize = fileSize,
        };
    }

    private static int CountNonCutoffParts(Plate plate) =>
        plate.Parts.Count(part => !part.BaseDrawing.IsCutOff);
}
