using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using netDxf;
using OpenNest.Geometry;

namespace OpenNest.IO
{
    public class DxfImporter
    {
        public int SplinePrecision { get; set; }

        public DxfImporter()
        {
        }

        private List<Entity> GetGeometry(DxfDocument doc)
        {
            var entities = new List<Entity>();
            var lines = new List<Line>(doc.Entities.Lines.Count());
            var arcs = new List<Arc>(doc.Entities.Arcs.Count());

            foreach (var spline in doc.Entities.Splines)
                lines.AddRange(spline.ToOpenNest(SplinePrecision));

            foreach (var polyline in doc.Entities.Polylines2D)
                lines.AddRange(polyline.ToOpenNest());

            foreach (var ellipse in doc.Entities.Ellipses)
                lines.AddRange(ellipse.ToOpenNest(SplinePrecision));

            foreach (var line in doc.Entities.Lines)
                lines.Add(line.ToOpenNest());

            foreach (var arc in doc.Entities.Arcs)
                arcs.Add(arc.ToOpenNest());

            foreach (var circle in doc.Entities.Circles)
                entities.Add(circle.ToOpenNest());

            foreach (var polyline in doc.Entities.Polylines3D)
                lines.AddRange(polyline.ToOpenNest());

            Helper.Optimize(lines);
            Helper.Optimize(arcs);

            entities.AddRange(lines);
            entities.AddRange(arcs);

            return entities;
        }

        public bool GetGeometry(Stream stream, out List<Entity> geometry)
        {
            bool success = false;

            try
            {
                var doc = DxfDocument.Load(stream);
                geometry = GetGeometry(doc);
                success = true;
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
            Stream stream = null;
            bool success = false;

            try
            {
                var doc = DxfDocument.Load(path);
                geometry = GetGeometry(doc);
                success = true;
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
    }
}
