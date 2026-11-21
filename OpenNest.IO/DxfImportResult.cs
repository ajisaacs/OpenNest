using System.Collections.Generic;
using ACadSharp;
using OpenNest.Bending;
using OpenNest.Geometry;

namespace OpenNest.IO
{
    public class DxfImportResult
    {
        public List<Entity> Entities { get; set; } = new();

        /// <summary>
        /// The file's geometry on every layer, before any cleanup. These are separate
        /// instances from <see cref="Entities"/>.
        /// </summary>
        public List<Entity> RawEntities { get; set; } = new();
        public CadDocument Document { get; set; }

        /// <summary>
        /// Bends the file states outright, as an SVG's BEND layer does; null when bends are
        /// detected from <see cref="Document"/> instead.
        /// </summary>
        public List<Bend> Bends { get; set; }

        /// <summary>Names the file gives to regions of its geometry, such as SVG titles; empty when none.</summary>
        public List<LabeledRegion> Labels { get; set; } = new();

        public List<string> Warnings { get; set; } = new();
    }
}
