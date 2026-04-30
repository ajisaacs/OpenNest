using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenNest.Bending;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO.Bending;
using OpenNest.Math;

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

            RemoveDuplicateArcs(dxf.Entities);

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
                SourcePath = path,
                Name = options.Name ?? Path.GetFileNameWithoutExtension(path),
                Document = dxf.Document,
            };
        }

        /// <summary>
        /// Convenience for headless callers: Import a file and build a Drawing
        /// in a single call, using all loaded entities and detected bends.
        /// </summary>
        public static Drawing ImportDrawing(string path, CadImportOptions options = null)
        {
            options ??= CadImportOptions.Default;
            var result = Import(path, options);
            return BuildDrawing(
                result,
                result.Entities,
                result.Bends,
                options.Quantity,
                options.Customer,
                editedProgram: null);
        }

        /// <summary>
        /// Build a fully-populated <see cref="Drawing"/> from an import result plus
        /// the caller's current entity and bend state. UI callers pass the currently
        /// visible subset; headless callers pass the full lists.
        ///
        /// The produced drawing has:
        /// - Program generated from the visible entities, with its first rapid moved
        ///   to the origin and the pierce location stored in Source.Offset
        /// - SourceEntities containing all non-bend-source entities from the result
        /// - SuppressedEntityIds containing entities whose layer or IsVisible is false
        /// - Bends copied from the provided list
        /// - Customer, Quantity, Source.Path from options / result
        /// </summary>
        /// <param name="result">Import result from <see cref="Import"/>.</param>
        /// <param name="entities">
        /// Entities to build the program from. Typically the currently visible subset.
        /// </param>
        /// <param name="bends">Bends to attach to the drawing.</param>
        /// <param name="quantity">Required quantity.</param>
        /// <param name="customer">Customer name, or null.</param>
        /// <param name="editedProgram">
        /// When non-null, replaces the generated program (used by the UI to honor
        /// in-place G-code edits). Source.Offset is still populated from the
        /// generated program so round-trips stay consistent.
        /// </param>
        public static Drawing BuildDrawing(
            CadImportResult result,
            IEnumerable<Entity> entities,
            IEnumerable<Bend> bends,
            int quantity,
            string customer,
            OpenNest.CNC.Program editedProgram)
        {
            var visible = entities as IList<Entity> ?? new List<Entity>(entities);
            var bendList = bends as IList<Bend> ?? new List<Bend>(bends);

            var normalized = ShapeProfile.NormalizeEntities(visible);
            var pgm = ConvertGeometry.ToProgram(normalized);

            var offset = Vector.Zero;
            if (pgm != null && pgm.Codes.Count > 0 && pgm[0].Type == OpenNest.CNC.CodeType.RapidMove)
            {
                var rapid = (OpenNest.CNC.RapidMove)pgm[0];
                offset = rapid.EndPoint;
                pgm.Offset(-offset);
            }

            var drawing = new Drawing(result.Name)
            {
                Color = Drawing.GetNextColor(),
                Customer = customer,
            };
            drawing.Source.Path = result.SourcePath;
            drawing.Source.Offset = offset;
            drawing.Quantity.Required = quantity;
            drawing.Bends.AddRange(bendList);
            drawing.Program = editedProgram ?? pgm;

            var bendSources = new HashSet<Entity>(
                bendList.Where(b => b.SourceEntity != null).Select(b => b.SourceEntity));

            drawing.SourceEntities = result.Entities
                .Where(e => !bendSources.Contains(e))
                .ToList();

            drawing.SuppressedEntityIds = new HashSet<System.Guid>(
                drawing.SourceEntities
                    .Where(e => !(e.Layer != null && e.Layer.IsVisible && e.IsVisible))
                    .Select(e => e.Id));

            return drawing;
        }

        internal static void RemoveDuplicateArcs(List<Entity> entities)
        {
            var circles = entities.OfType<Circle>().ToList();
            var arcs = entities.OfType<Arc>().ToList();
            var arcsToRemove = new List<Arc>();

            foreach (var arc in arcs)
            {
                foreach (var circle in circles)
                {
                    if (arc.Layer?.Name != circle.Layer?.Name)
                        continue;

                    if (!arc.Center.DistanceTo(circle.Center).IsEqualTo(0))
                        continue;

                    if (!arc.Radius.IsEqualTo(circle.Radius))
                        continue;

                    arcsToRemove.Add(arc);
                    break;
                }
            }

            foreach (var arc in arcsToRemove)
                entities.Remove(arc);
        }
    }
}
