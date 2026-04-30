using System.Collections.Generic;
using ACadSharp;
using OpenNest.Bending;
using OpenNest.Geometry;

namespace OpenNest.IO
{
    /// <summary>
    /// Intermediate result of <see cref="CadImporter.Import"/>. Holds raw loaded
    /// geometry and detected bends. Callers may mutate <see cref="Entities"/> and
    /// <see cref="Bends"/> before passing to <see cref="CadImporter.BuildDrawing"/>.
    /// </summary>
    public class CadImportResult
    {
        /// <summary>
        /// All entities loaded from the source file, including promoted bend
        /// source entities. Mutable.
        /// </summary>
        public List<Entity> Entities { get; set; } = new List<Entity>();

        /// <summary>
        /// Bends detected during import. Mutable — callers may add, remove,
        /// or replace entries before building the drawing.
        /// </summary>
        public List<Bend> Bends { get; set; } = new List<Bend>();

        /// <summary>
        /// Bounding box of <see cref="Entities"/> at import time. May be stale
        /// if callers mutate <see cref="Entities"/>; recompute if needed.
        /// </summary>
        public Box Bounds { get; set; }

        /// <summary>
        /// Absolute path to the source file.
        /// </summary>
        public string SourcePath { get; set; }

        /// <summary>
        /// Default drawing name (filename without extension, unless overridden).
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The raw CAD document from the source file. Available for callers
        /// that need access to non-geometry entities (e.g., text annotations).
        /// </summary>
        public CadDocument Document { get; set; }
    }
}
