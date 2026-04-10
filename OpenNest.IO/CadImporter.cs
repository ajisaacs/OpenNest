using System.Collections.Generic;
using System.IO;
using OpenNest.Bending;
using OpenNest.Geometry;
using OpenNest.IO.Bending;

namespace OpenNest.IO
{
    /// <summary>
    /// Shared service that converts a CAD source file into a fully-populated
    /// <see cref="Drawing"/>. Used by the UI, console, MCP, API, and training
    /// tools so all code paths produce identical drawings.
    /// </summary>
    public static class CadImporter
    {
        /// <summary>
        /// Load a DXF file, run bend detection, and return a mutable result
        /// ready for interactive editing or direct conversion to a Drawing.
        /// </summary>
        public static CadImportResult Import(string path, CadImportOptions options = null)
        {
            options ??= CadImportOptions.Default;

            var dxf = Dxf.Import(path);

            var bends = new List<Bend>();
            if (options.DetectBends && dxf.Document != null)
            {
                bends = options.BendDetectorName == null
                    ? BendDetectorRegistry.AutoDetect(dxf.Document)
                    : BendDetectorRegistry.GetByName(options.BendDetectorName)
                        ?.DetectBends(dxf.Document)
                      ?? new List<Bend>();
            }

            Bend.UpdateEtchEntities(dxf.Entities, bends);

            return new CadImportResult
            {
                Entities = dxf.Entities,
                Bends = bends,
                Bounds = dxf.Entities.GetBoundingBox(),
                Document = dxf.Document,
                SourcePath = path,
                Name = options.Name ?? Path.GetFileNameWithoutExtension(path),
            };
        }
    }
}
