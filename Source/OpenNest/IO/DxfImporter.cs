using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            var lines = new List<Line>(doc.Lines.Count);
            var arcs = new List<Arc>(doc.Arcs.Count);

            foreach (var spline in doc.Splines)
                lines.AddRange(spline.ToOpenNest(SplinePrecision));

            foreach (var polyline in doc.LwPolylines)
                lines.AddRange(polyline.ToOpenNest());

            foreach (var ellipse in doc.Ellipses)
                lines.AddRange(ellipse.ToOpenNest(SplinePrecision));

            foreach (var line in doc.Lines)
                lines.Add(line.ToOpenNest());

            foreach (var arc in doc.Arcs)
                arcs.Add(arc.ToOpenNest());

            foreach (var circle in doc.Circles)
                entities.Add(circle.ToOpenNest());

            foreach (var polyline in doc.Polylines)
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
