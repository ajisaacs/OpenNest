using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using OpenNest.CNC;

namespace OpenNest.IO
{
    public sealed class NestWriter
    {
        /// <summary>
        /// Number of decimal places the output is round to. 
        /// This number must have more decimal places than Tolerance.Epsilon
        /// </summary>
        private const int OutputPrecision = 10;

        private readonly Nest nest;
        private ZipArchive zipArchive;
        private Dictionary<int, Drawing> drawingDict;

        public NestWriter(Nest nest)
        {
            this.drawingDict = new Dictionary<int, Drawing>();
            this.nest = nest;
        }

        public bool Write(string file)
        {
            this.nest.DateLastModified = DateTime.Now;

            SetDrawingIds();

            using (var fileStream = new FileStream(file, FileMode.Create))
            using (zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Create))
            {
                AddNestInfo();
                AddPlates();
                AddPlateInfo();
                AddDrawings();
                AddDrawingInfo();
            }

            return true;
        }

        private void SetDrawingIds()
        {
            int id = 1;

            foreach (var drawing in nest.Drawings)
            {
                drawingDict.Add(id, drawing);
                id++;
            }
        }

        private void AddNestInfo()
        {
            var stream = new MemoryStream();
            var writer = XmlWriter.Create(stream, new XmlWriterSettings()
            {
                Indent = true
            });

            writer.WriteStartDocument();
            writer.WriteStartElement("Nest");
            writer.WriteAttributeString("name", nest.Name);

            writer.WriteElementString("Units", nest.Units.ToString());
            writer.WriteElementString("Customer", nest.Customer);
            writer.WriteElementString("DateCreated", nest.DateCreated.ToString());
            writer.WriteElementString("DateLastModified", nest.DateLastModified.ToString());

            writer.WriteStartElement("DefaultPlate");
            writer.WriteElementString("Size", nest.PlateDefaults.Size.ToString());
            writer.WriteElementString("Thickness", nest.PlateDefaults.Thickness.ToString());
            writer.WriteElementString("Quadrant", nest.PlateDefaults.Quadrant.ToString());
            writer.WriteElementString("PartSpacing", nest.PlateDefaults.PartSpacing.ToString());

            writer.WriteStartElement("Material");
            writer.WriteElementString("Name", nest.PlateDefaults.Material.Name);
            writer.WriteElementString("Grade", nest.PlateDefaults.Material.Grade);
            writer.WriteElementString("Density", nest.PlateDefaults.Material.Density.ToString());
            writer.WriteEndElement();

            writer.WriteStartElement("EdgeSpacing");
            writer.WriteElementString("Left", nest.PlateDefaults.EdgeSpacing.Left.ToString());
            writer.WriteElementString("Top", nest.PlateDefaults.EdgeSpacing.Top.ToString());
            writer.WriteElementString("Right", nest.PlateDefaults.EdgeSpacing.Right.ToString());
            writer.WriteElementString("Bottom", nest.PlateDefaults.EdgeSpacing.Bottom.ToString());
            writer.WriteEndElement();

            writer.WriteElementString("Notes", Uri.EscapeDataString(nest.Notes));

            writer.WriteEndElement(); // DefaultPlate
            writer.WriteEndElement(); // Nest

            writer.WriteEndDocument();

            writer.Flush();
            writer.Close();

            stream.Position = 0;

            var entry = zipArchive.CreateEntry("info");
            using (var entryStream = entry.Open())
            {
                stream.CopyTo(entryStream);
            }
        }

        private void AddPlates()
        {
            int num = 1;

            foreach (var plate in nest.Plates)
            {
                var stream = new MemoryStream();
                var name = string.Format("plate-{0}", num.ToString().PadLeft(3, '0'));

                WritePlate(stream, plate);

                var entry = zipArchive.CreateEntry(name);
                using (var entryStream = entry.Open())
                {
                    stream.CopyTo(entryStream);
                }

                num++;
            }
        }

        private void AddPlateInfo()
        {
            var stream = new MemoryStream();
            var writer = XmlWriter.Create(stream, new XmlWriterSettings()
            {
                Indent = true
            });

            writer.WriteStartDocument();
            writer.WriteStartElement("Plates");
            writer.WriteAttributeString("count", nest.Plates.Count.ToString());

            for (int i = 0; i < nest.Plates.Count; ++i)
            {
                var plate = nest.Plates[i];

                writer.WriteStartElement("Plate");
                writer.WriteAttributeString("id", (i + 1).ToString());

                writer.WriteElementString("Quadrant", plate.Quadrant.ToString());
                writer.WriteElementString("Thickness", plate.Thickness.ToString());
                writer.WriteElementString("Size", plate.Size.ToString());
                writer.WriteElementString("Qty", plate.Quantity.ToString());
                writer.WriteElementString("PartSpacing", plate.PartSpacing.ToString());

                writer.WriteStartElement("Material");
                writer.WriteElementString("Name", plate.Material.Name);
                writer.WriteElementString("Grade", plate.Material.Grade);
                writer.WriteElementString("Density", plate.Material.Density.ToString());
                writer.WriteEndElement();

                writer.WriteStartElement("EdgeSpacing");
                writer.WriteElementString("Left", plate.EdgeSpacing.Left.ToString());
                writer.WriteElementString("Top", plate.EdgeSpacing.Top.ToString());
                writer.WriteElementString("Right", plate.EdgeSpacing.Right.ToString());
                writer.WriteElementString("Bottom", plate.EdgeSpacing.Bottom.ToString());
                writer.WriteEndElement();

                writer.WriteEndElement(); // Plate
                writer.Flush();
            }

            writer.WriteEndElement(); // Plates
            writer.WriteEndDocument();

            writer.Flush();
            writer.Close();

            stream.Position = 0;

            var entry = zipArchive.CreateEntry("plate-info");
            using (var entryStream = entry.Open())
            {
                stream.CopyTo(entryStream);
            }
        }

        private void AddDrawings()
        {
            int num = 1;

            foreach (var dwg in nest.Drawings)
            {
                var stream = new MemoryStream();
                var name = string.Format("program-{0}", num.ToString().PadLeft(3, '0'));

                WriteDrawing(stream, dwg);

                var entry = zipArchive.CreateEntry(name);
                using (var entryStream = entry.Open())
                {
                    stream.CopyTo(entryStream);
                }

                num++;
            }
        }

        private void AddDrawingInfo()
        {
            var stream = new MemoryStream();
            var writer = XmlWriter.Create(stream, new XmlWriterSettings()
            {
                Indent = true
            });

            writer.WriteStartDocument();
            writer.WriteStartElement("Drawings");
            writer.WriteAttributeString("count", nest.Drawings.Count.ToString());

            int id = 1;

            foreach (var drawing in nest.Drawings)
            {
                writer.WriteStartElement("Drawing");
                writer.WriteAttributeString("id", id.ToString());
                writer.WriteAttributeString("name", drawing.Name);

                writer.WriteElementString("Customer", drawing.Customer);
                writer.WriteElementString("Color", string.Format("{0}, {1}, {2}, {3}", drawing.Color.A, drawing.Color.R, drawing.Color.G, drawing.Color.B));

                writer.WriteStartElement("Quantity");
                writer.WriteElementString("Required", drawing.Quantity.Required.ToString());
                writer.WriteElementString("Nested", drawing.Quantity.Nested.ToString());
                writer.WriteEndElement();

                writer.WriteStartElement("Material");
                writer.WriteElementString("Name", drawing.Material.Name);
                writer.WriteElementString("Grade", drawing.Material.Grade);
                writer.WriteElementString("Density", drawing.Material.Density.ToString());
                writer.WriteEndElement();

                writer.WriteStartElement("Source");
                writer.WriteElementString("Path", drawing.Source.Path);
                writer.WriteElementString("Offset", string.Format("{0}, {1}", 
                    drawing.Source.Offset.X, 
                    drawing.Source.Offset.Y));
                writer.WriteEndElement(); // Source

                writer.WriteEndElement(); // Drawing

                id++;
            }

            writer.WriteEndElement(); // Drawings
            writer.WriteEndDocument();

            writer.Flush();
            writer.Close();

            stream.Position = 0;

            var entry = zipArchive.CreateEntry("drawing-info");
            using (var entryStream = entry.Open())
            {
                stream.CopyTo(entryStream);
            }
        }

        private void WritePlate(Stream stream, Plate plate)
        {
            var writer = new StreamWriter(stream);
            writer.AutoFlush = true;
            writer.WriteLine("G90");

            foreach (var part in plate.Parts)
            {
                var match = drawingDict.Where(dwg => dwg.Value == part.BaseDrawing).FirstOrDefault();
                var id = match.Key;

                writer.WriteLine("G00X{0}Y{1}", part.Location.X, part.Location.Y);
                writer.WriteLine("G65P{0}R{1}", id, Angle.ToDegrees(part.Rotation));
            }

            stream.Position = 0;
        }

        private void WriteDrawing(Stream stream, Drawing drawing)
        {
            var program = drawing.Program;
            var writer = new StreamWriter(stream);
            writer.AutoFlush = true;

            writer.WriteLine(program.Mode == Mode.Absolute ? "G90" : "G91");

            for (int i = 0; i < drawing.Program.Length; ++i)
            {
                var code = drawing.Program[i];
                writer.WriteLine(GetCodeString(code));
            }

            stream.Position = 0;
        }

        private string GetCodeString(ICode code)
        {
            switch (code.Type)
            {
                case CodeType.ArcMove:
                    {
                        var sb = new StringBuilder();
                        var arcMove = (ArcMove)code;

                        if (arcMove.Rotation == RotationType.CW)
                        {
                            sb.Append(string.Format("G02X{0}Y{1}I{2}J{3}",
                                Math.Round(arcMove.EndPoint.X, OutputPrecision),
                                Math.Round(arcMove.EndPoint.Y, OutputPrecision),
                                Math.Round(arcMove.CenterPoint.X, OutputPrecision),
                                Math.Round(arcMove.CenterPoint.Y, OutputPrecision)));
                        }
                        else
                        {
                            sb.Append(string.Format("G03X{0}Y{1}I{2}J{3}",
                                Math.Round(arcMove.EndPoint.X, OutputPrecision),
                                Math.Round(arcMove.EndPoint.Y, OutputPrecision),
                                Math.Round(arcMove.CenterPoint.X, OutputPrecision),
                                Math.Round(arcMove.CenterPoint.Y, OutputPrecision)));
                        }

                        if (arcMove.Layer != LayerType.Cut)
                            sb.Append(GetLayerString(arcMove.Layer));

                        return sb.ToString();
                    }

                case CodeType.Comment:
                    {
                        var comment = (Comment)code;
                        return ":" + comment.Value;
                    }

                case CodeType.LinearMove:
                    {
                        var sb = new StringBuilder();
                        var linearMove = (LinearMove)code;

                        sb.Append(string.Format("G01X{0}Y{1}",
                            Math.Round(linearMove.EndPoint.X, OutputPrecision),
                            Math.Round(linearMove.EndPoint.Y, OutputPrecision)));

                        if (linearMove.Layer != LayerType.Cut)
                            sb.Append(GetLayerString(linearMove.Layer));

                        return sb.ToString();
                    }

                case CodeType.RapidMove:
                    {
                        var rapidMove = (RapidMove)code;

                        return string.Format("G00X{0}Y{1}",
                            Math.Round(rapidMove.EndPoint.X, OutputPrecision),
                            Math.Round(rapidMove.EndPoint.Y, OutputPrecision));
                    }

                case CodeType.SetFeedrate:
                    {
                        var setFeedrate = (Feedrate)code;
                        return "F" + setFeedrate.Value;
                    }

                case CodeType.SetKerf:
                    {
                        var setKerf = (Kerf)code;

                        switch (setKerf.Value)
                        {
                            case KerfType.None: return "G40";
                            case KerfType.Left: return "G41";
                            case KerfType.Right: return "G42";
                        }

                        break;
                    }

                case CodeType.SubProgramCall:
                    {
                        var subProgramCall = (SubProgramCall)code;
                        break;
                    }
            }

            return string.Empty;
        }

        private string GetLayerString(LayerType layer)
        {
            switch (layer)
            {
                case LayerType.Display:
                    return ":DISPLAY";

                case LayerType.Leadin:
                    return ":LEADIN";

                case LayerType.Leadout:
                    return ":LEADOUT";

                case LayerType.Scribe:
                    return ":SCRIBE";

                default:
                    return string.Empty;
            }
        }
    }
}
