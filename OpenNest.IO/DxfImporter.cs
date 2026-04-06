using ACadSharp;
using ACadSharp.IO;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace OpenNest.IO
{
    public class DxfImporter
    {
        public DxfImporter()
        {
        }

        private List<Entity> GetGeometry(CadDocument doc)
        {
            var entities = new List<Entity>();
            var lines = new List<Line>();
            var arcs = new List<Arc>();

            foreach (var entity in doc.Entities)
            {
                // Skip bend/etch entities — bends are converted to Bend objects
                // separately via bend detection, and etch marks are generated from
                // bends during DXF export. Neither should be treated as cut geometry.
                if (IsNonCutLayer(entity.Layer?.Name))
                    continue;

                switch (entity)
                {
                    case ACadSharp.Entities.Line line:
                        lines.Add(line.ToOpenNest());
                        break;

                    case ACadSharp.Entities.Arc arc:
                        arcs.Add(arc.ToOpenNest());
                        break;

                    case ACadSharp.Entities.Circle circle:
                        entities.Add(circle.ToOpenNest());
                        break;

                    case ACadSharp.Entities.Spline spline:
                        foreach (var e in spline.ToOpenNest())
                        {
                            if (e is Line l) lines.Add(l);
                            else if (e is Arc a) arcs.Add(a);
                        }
                        break;

                    case ACadSharp.Entities.LwPolyline lwPolyline:
                        lines.AddRange(lwPolyline.ToOpenNest());
                        break;

                    case ACadSharp.Entities.Polyline polyline:
                        lines.AddRange(polyline.ToOpenNest());
                        break;

                    case ACadSharp.Entities.Ellipse ellipse:
                        foreach (var e in ellipse.ToOpenNest())
                        {
                            if (e is Line l) lines.Add(l);
                            else if (e is Arc a) arcs.Add(a);
                        }
                        break;
                }
            }

            GeometryOptimizer.Optimize(lines);
            GeometryOptimizer.Optimize(arcs);

            entities.AddRange(lines);
            entities.AddRange(arcs);

            return entities;
        }

        /// <summary>
        /// Imports a DXF file, returning both converted entities and the raw CadDocument
        /// for bend detection. The CadDocument is NOT disposed — caller can use it for
        /// additional analysis (e.g., MText extraction for bend notes).
        /// </summary>
        public DxfImportResult Import(string path)
        {
            using var reader = new DxfReader(path);
            var doc = reader.Read();
            var entities = GetGeometry(doc);

            return new DxfImportResult
            {
                Entities = entities,
                Document = doc
            };
        }

        public bool GetGeometry(Stream stream, out List<Entity> geometry)
        {
            var success = false;

            try
            {
                using (var reader = new DxfReader(stream))
                {
                    var doc = reader.Read();
                    geometry = GetGeometry(doc);
                    success = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                geometry = new List<Entity>();
            }
            finally
            {
                if (stream != null)
                    stream.Close();
            }

            return success;
        }

        public bool GetGeometry(string path, out List<Entity> geometry)
        {
            var success = false;

            try
            {
                using (var reader = new DxfReader(path))
                {
                    var doc = reader.Read();
                    geometry = GetGeometry(doc);
                    success = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                geometry = new List<Entity>();
            }

            return success;
        }

        private static bool IsNonCutLayer(string layerName)
        {
            return string.Equals(layerName, "BEND", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(layerName, "ETCH", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
