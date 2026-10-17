using System;

namespace OpenNest
{
    /// <summary>
    /// Where a nest drawing came from in a server drawing library: the exact revision that
    /// was copied in and a snapshot of its library details at that time. The nest owns its
    /// copy; the link never changes the library and the library never changes the copy.
    /// Immutable, so drawing copies may share one instance.
    /// </summary>
    public sealed class DrawingLibraryLink
    {
        public DrawingLibraryLink(string serverKey, Guid id, int revision, CutType cutType, double thickness,
            string materialName, string materialGrade, Units units, string notes, string packageSha256)
        {
            if (string.IsNullOrWhiteSpace(serverKey))
                throw new ArgumentException("A library link needs the server it came from.", nameof(serverKey));
            if (id == Guid.Empty)
                throw new ArgumentException("A library link needs a drawing id.", nameof(id));
            if (revision < 1)
                throw new ArgumentOutOfRangeException(nameof(revision), revision, "Revisions start at 1.");
            if (!Enum.IsDefined(cutType))
                throw new ArgumentOutOfRangeException(nameof(cutType), cutType, "Unsupported cut type.");
            if (!double.IsFinite(thickness) || thickness < 0)
                throw new ArgumentOutOfRangeException(nameof(thickness), thickness, "Thickness must be finite and not negative.");
            if (!Enum.IsDefined(units))
                throw new ArgumentOutOfRangeException(nameof(units), units, "Unsupported units.");

            ServerKey = serverKey;
            Id = id;
            Revision = revision;
            CutType = cutType;
            Thickness = thickness;
            MaterialName = materialName ?? "";
            MaterialGrade = materialGrade ?? "";
            Units = units;
            Notes = notes ?? "";
            PackageSha256 = packageSha256 ?? "";
        }

        /// <summary>Normalized base URL of the server holding the library (no trailing slash).</summary>
        public string ServerKey { get; }

        public Guid Id { get; }

        /// <summary>The library revision this drawing is a copy of.</summary>
        public int Revision { get; }

        public CutType CutType { get; }

        /// <summary>Library thickness in <see cref="Units"/>.</summary>
        public double Thickness { get; }

        public string MaterialName { get; }

        public string MaterialGrade { get; }

        public Units Units { get; }

        public string Notes { get; }

        /// <summary>Lower-case hex SHA-256 of the library package for <see cref="Revision"/>.</summary>
        public string PackageSha256 { get; }

        /// <summary>True when both links name the same revision of the same library drawing.</summary>
        public bool IsSameRevision(DrawingLibraryLink other) =>
            other != null && Id == other.Id && Revision == other.Revision
            && string.Equals(ServerKey, other.ServerKey, StringComparison.Ordinal);
    }
}
