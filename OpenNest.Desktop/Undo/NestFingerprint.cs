using System;
using System.Runtime.CompilerServices;

namespace OpenNest.Undo
{
    /// <summary>
    /// A cheap hash of what edits usually change: plates, their parts' poses, programs and
    /// cut-offs, and the drawings with their required quantities. Two equal fingerprints do not
    /// prove two nests equal; a differing one means the nest changed since it was taken. Objects
    /// count by reference, so compare fingerprints of one live nest, never across a restore.
    /// Derived values (nested counts, colors, the modified date) are left out.
    /// </summary>
    internal static class NestFingerprint
    {
        public static int Compute(Nest nest)
        {
            var hash = new HashCode();
            hash.Add(nest.Plates.Count);
            foreach (var plate in nest.Plates)
            {
                hash.Add(plate.Size.Width);
                hash.Add(plate.Size.Length);
                hash.Add(plate.Quantity);
                hash.Add(plate.PartSpacing);
                hash.Add(RuntimeHelpers.GetHashCode(plate.CuttingParameters));
                hash.Add(plate.ClampZones.Count);
                hash.Add(plate.Parts.Count);
                foreach (var part in plate.Parts)
                {
                    hash.Add(RuntimeHelpers.GetHashCode(part.BaseDrawing));
                    hash.Add(RuntimeHelpers.GetHashCode(part.Program));
                    hash.Add(part.Location.X);
                    hash.Add(part.Location.Y);
                    hash.Add(part.Rotation);
                    hash.Add(part.HasManualLeadIns);
                    hash.Add(part.LeadInsLocked);
                }
                hash.Add(plate.CutOffs.Count);
                foreach (var cutOff in plate.CutOffs)
                {
                    hash.Add(cutOff.Position.X);
                    hash.Add(cutOff.Position.Y);
                    hash.Add(cutOff.Axis);
                }
            }

            hash.Add(nest.Drawings.Count);
            foreach (var drawing in nest.Drawings)
            {
                hash.Add(RuntimeHelpers.GetHashCode(drawing));
                hash.Add(RuntimeHelpers.GetHashCode(drawing.Program));
                hash.Add(drawing.Quantity.Required);
            }
            return hash.ToHashCode();
        }
    }
}
