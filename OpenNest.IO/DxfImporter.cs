using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ACadSharp;
using ACadSharp.IO;
using OpenNest.Geometry;

namespace OpenNest.IO
{
    public class DxfImporter
    {
        public int SplinePrecision { get; set; }

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
                        lines.AddRange(spline.ToOpenNest());
                        break;

                    case ACadSharp.Entities.LwPolyline lwPolyline:
                        lines.AddRange(lwPolyline.ToOpenNest());
                        break;

                    case ACadSharp.Entities.Polyline polyline:
                        lines.AddRange(polyline.ToOpenNest());
                        break;

                    case ACadSharp.Entities.Ellipse ellipse:
                        lines.AddRange(ellipse.ToOpenNest(SplinePrecision));
                        break;
                }
            }

            GeometryOptimizer.Optimize(lines);
            GeometryOptimizer.Optimize(arcs);

            entities.AddRange(lines);
            entities.AddRange(arcs);

            return entities;
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
    }
}
