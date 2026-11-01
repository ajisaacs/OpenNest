using System.Collections.Generic;
using ACadSharp;
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
        public List<string> Warnings { get; set; } = new();
    }
}
