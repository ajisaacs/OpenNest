using System;
using System.Collections.Generic;
using System.IO;

namespace OpenNest.IO
{
    /// <summary>
    /// An in-memory copy of a whole nest for undo: the <c>.nest</c> archive a save would
    /// write, except that empty plates are kept, best fits are left out (they are a cache)
    /// and the nest's modified date is not stamped.
    /// </summary>
    public sealed class NestSnapshot
    {
        private readonly byte[] bytes;

        private NestSnapshot(byte[] bytes) => this.bytes = bytes;

        public long SizeInBytes => bytes.Length;

        /// <summary>The archive, for tests and diagnostics; never written over a user's file.</summary>
        public ReadOnlySpan<byte> Bytes => bytes;

        public static NestSnapshot Capture(Nest nest)
        {
            ArgumentNullException.ThrowIfNull(nest);
            using var stream = new MemoryStream();
            new NestWriter(nest) { PreserveDateLastModified = true, KeepEmptyPlates = true }.Write(stream);
            return new NestSnapshot(stream.ToArray());
        }

        /// <summary>
        /// Reads the copy back as a new nest. Each drawing of <paramref name="live"/> whose saved
        /// content equals a drawing in the copy is used in its place, so caches keyed by the
        /// drawing (best fits, fill results), its color and its id survive; a drawing whose
        /// content changed comes from the copy. Reused drawings' nested counts then follow the
        /// returned nest, which replaces <paramref name="live"/>.
        /// </summary>
        public Nest Restore(Nest live = null)
        {
            var reusable = live == null ? null : ReusableDrawings(live);
            try
            {
                var nest = new NestReader(new MemoryStream(bytes, writable: false)) { ReusableDrawings = reusable }.Read();
                nest.UpdateDrawingQuantities();
                return nest;
            }
            catch when (live != null)
            {
                // Reading adds parts to reused drawings; give the live nest its counts back.
                live.UpdateDrawingQuantities();
                throw;
            }
        }

        // Fingerprints include the name, and a nest's drawings have distinct names, so each
        // fingerprint names one drawing.
        private static Dictionary<string, Drawing> ReusableDrawings(Nest live)
        {
            var fingerprints = NestWriter.GetDrawingFingerprints(live);
            var result = new Dictionary<string, Drawing>(StringComparer.Ordinal);
            var i = 0;
            // Same order as the fingerprints: both follow the collection's enumeration.
            foreach (var drawing in live.Drawings)
                result.TryAdd(fingerprints[i++], drawing);
            return result;
        }
    }
}
