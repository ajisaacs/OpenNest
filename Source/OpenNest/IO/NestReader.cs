using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Ionic.Zip;
using OpenNest.CNC;

namespace OpenNest.IO
{
    public sealed class NestReader
    {
        private ZipFile zipFile;
        private Dictionary<int, Plate> plateDict;
        private Dictionary<int, Drawing> drawingDict;
        private Dictionary<int, Program> programDict;
        private Dictionary<int, Program> plateProgramDict;
        private Stream stream;
        private Nest nest;

        private NestReader()
        {
            plateDict = new Dictionary<int, Plate>();
            drawingDict = new Dictionary<int, Drawing>();
            programDict = new Dictionary<int, Program>();
            plateProgramDict = new Dictionary<int, Program>();
            nest = new Nest();
        }

        public NestReader(string file)
            : this()
        {
            stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite);
            zipFile = ZipFile.Read(stream);
        }

        public NestReader(Stream stream)
            : this()
        {
            zipFile = ZipFile.Read(stream);
        }

        public Nest Read()
        {
            const string plateExtensionPattern = "plate-\\d\\d\\d";
            const string programExtensionPattern = "program-\\d\\d\\d";

            foreach (var entry in zipFile.Entries)
            {
                var memstream = new MemoryStream();
                entry.Extract(memstream);

                memstream.Position = 0;

                switch (entry.FileName)
                {
                    case "info":
                        ReadNestInfo(memstream);
                        continue;

                    case "drawing-info":
                        ReadDrawingInfo(memstream);
                        continue;

                    case "plate-info":
                        ReadPlateInfo(memstream);
                        continue;
                }

                if (Regex.IsMatch(entry.FileName, programExtensionPattern))
                {
                    ReadProgram(memstream, entry.FileName);
                    continue;
                }

                if (Regex.IsMatch(entry.FileName, plateExtensionPattern))
                {
                    ReadPlate(memstream, entry.FileName);
                    continue;
                }
            }

            LinkProgramsToDrawings();
            LinkPartsToPlates();

            AddPlatesToNest();
            AddDrawingsToNest();

            stream.Close();

            return nest;
        }

        private void ReadNestInfo(Stream stream)
        {
            var reader = XmlReader.Create(stream);
            var spacing = new Spacing();

            while (reader.Read())
            {
                if (!reader.IsStartElement())
                    continue;

                switch (reader.Name)
                {
                    case "Nest":
                        nest.Name = reader["name"];
                        break;

                    case "Units":
                        Units units;
                        TryParseEnum<Units>(reader.ReadString(), out units);
                        nest.Units = units;
                        break;

                    case "Customer":
                        nest.Customer = reader.ReadString();
                        break;

                    case "DateCreated":
                        nest.DateCreated = DateTime.Parse(reader.ReadString());
                        break;

                    case "DateLastModified":
                        nest.DateLastModified = DateTime.Parse(reader.ReadString());
                        break;

                    case "Notes":
                        nest.Notes = Uri.UnescapeDataString(reader.ReadString());
                        break;

                    case "Size":
                        nest.PlateDefaults.Size = Size.Parse(reader.ReadString());
                        break;

                    case "Thickness":
                        nest.PlateDefaults.Thickness = double.Parse(reader.ReadString());
                        break;

                    case "Quadrant":
                        nest.PlateDefaults.Quadrant = int.Parse(reader.ReadString());
                        break;

                    case "PartSpacing":
                        nest.PlateDefaults.PartSpacing = double.Parse(reader.ReadString());
                        break;

                    case "Name":
                        nest.PlateDefaults.Material.Name = reader.ReadString();
                        break;

                    case "Grade":
                        nest.PlateDefaults.Material.Grade = reader.ReadString();
                        break;

                    case "Density":
                        nest.PlateDefaults.Material.Density = double.Parse(reader.ReadString());
                        break;

                    case "Left":
                        spacing.Left = double.Parse(reader.ReadString());
                        break;

                    case "Right":
                        spacing.Right = double.Parse(reader.ReadString());
                        break;

                    case "Top":
                        spacing.Top = double.Parse(reader.ReadString());
                        break;

                    case "Bottom":
                        spacing.Bottom = double.Parse(reader.ReadString());
                        break;
                }
            }

            reader.Close();
            nest.PlateDefaults.EdgeSpacing = spacing;
        }

        private void ReadDrawingInfo(Stream stream)
        {
            var reader = XmlReader.Create(stream);
            Drawing drawing = null;

            while (reader.Read())
            {
                if (!reader.IsStartElement())
                    continue;

                switch (reader.Name)
                {
                    case "Drawing":
                        var id = int.Parse(reader["id"]);
                        var name = reader["name"];

                        drawingDict.Add(id, (drawing = new Drawing(name)));
                        break;

                    case "Customer":
                        drawing.Customer = reader.ReadString();
                        break;

                    case "Color":
                        {
                            var parts = reader.ReadString().Split(',');
                        
                            if (parts.Length == 3)
                            {
                                byte r = byte.Parse(parts[0]);
                                byte g = byte.Parse(parts[1]);
                                byte b = byte.Parse(parts[2]);

                                drawing.Color = Color.FromArgb(r, g, b);
                            }
                            else if (parts.Length == 4)
                            {
                                byte a = byte.Parse(parts[0]);
                                byte r = byte.Parse(parts[1]);
                                byte g = byte.Parse(parts[2]);
                                byte b = byte.Parse(parts[3]);

                                drawing.Color = Color.FromArgb(a, r, g, b);
                            }
                        }
                        break;

                    case "Required":
                        drawing.Quantity.Required = int.Parse(reader.ReadString());
                        break;

                    case "Name":
                        drawing.Material.Name = reader.ReadString();
                        break;

                    case "Grade":
                        drawing.Material.Grade = reader.ReadString();
                        break;

                    case "Density":
                        drawing.Material.Density = double.Parse(reader.ReadString());
                        break;

                    case "Path":
                        drawing.Source.Path = reader.ReadString();
                        break;

                    case "Offset":
                        {
                            var parts = reader.ReadString().Split(',');

                            if (parts.Length != 2)
                                continue;

                            drawing.Source.Offset = new Vector(double.Parse(parts[0]), double.Parse(parts[1]));
                        }
                        break;
                }
            }

            reader.Close();
        }

        private void ReadPlateInfo(Stream stream)
        {
            var reader = XmlReader.Create(stream);
            var spacing = new Spacing();
            Plate plate = null;

            while (reader.Read())
            {
                if (!reader.IsStartElement())
                    continue;

                switch (reader.Name)
                {
                    case "Plate":
                        var id = int.Parse(reader["id"]);

                        if (plate != null)
                            plate.EdgeSpacing = spacing;

                        plateDict.Add(id, (plate = new Plate()));
                        break;

                    case "Size":
                        plate.Size = Size.Parse(reader.ReadString());
                        break;

                    case "Qty":
                        plate.Quantity = int.Parse(reader.ReadString());
                        break;

                    case "Thickness":
                        plate.Thickness = double.Parse(reader.ReadString());
                        break;

                    case "Quadrant":
                        plate.Quadrant = int.Parse(reader.ReadString());
                        break;

                    case "PartSpacing":
                        plate.PartSpacing = double.Parse(reader.ReadString());
                        break;

                    case "Name":
                        plate.Material.Name = reader.ReadString();
                        break;

                    case "Grade":
                        plate.Material.Grade = reader.ReadString();
                        break;

                    case "Density":
                        plate.Material.Density = double.Parse(reader.ReadString());
                        break;

                    case "Left":
                        spacing.Left = double.Parse(reader.ReadString());
                        break;

                    case "Right":
                        spacing.Right = double.Parse(reader.ReadString());
                        break;

                    case "Top":
                        spacing.Top = double.Parse(reader.ReadString());
                        break;

                    case "Bottom":
                        spacing.Bottom = double.Parse(reader.ReadString());
                        break;
                }
            }

            if (plate != null)
                plate.EdgeSpacing = spacing;
        }

        private void ReadProgram(Stream stream, string name)
        {
            var id = GetProgramId(name);
            var reader = new ProgramReader(stream);
            var pgm = reader.Read();
            programDict.Add(id, pgm);
        }

        private void ReadPlate(Stream stream, string name)
        {
            var id = GetPlateId(name);
            var reader = new ProgramReader(stream);
            var pgm = reader.Read();
            plateProgramDict.Add(id, pgm);
        }

        private void LinkProgramsToDrawings()
        {
            foreach (var drawingItem in drawingDict)
            {
                Program pgm;

                if (programDict.TryGetValue(drawingItem.Key, out pgm))
                    drawingItem.Value.Program = pgm;
            }
        }

        private void LinkPartsToPlates()
        {
            foreach (var plateProgram in plateProgramDict)
            {
                var parts = CreateParts(plateProgram.Value);

                Plate plate;

                if (!plateDict.TryGetValue(plateProgram.Key, out plate))
                    plate = new Plate();

                plate.Parts.AddRange(parts);
                plateDict[plateProgram.Key] = plate;
            }
        }

        private void AddPlatesToNest()
        {
            var plates = plateDict.OrderBy(i => i.Key).Select(i => i.Value).ToList();
            nest.Plates.AddRange(plates);
        }

        private void AddDrawingsToNest()
        {
            var drawings = drawingDict.OrderBy(i => i.Key).Select(i => i.Value).ToList();
            drawings.ForEach(d => nest.Drawings.Add(d));
        }

        private List<Part> CreateParts(Program pgm)
        {
            var parts = new List<Part>();
            var pos = Vector.Zero;

            for (int i = 0; i < pgm.Codes.Count; i++)
            {
                var code = pgm.Codes[i];

                switch (code.Type)
                {
                    case CodeType.RapidMove:
                        pos = ((RapidMove)code).EndPoint;
                        break;

                    case CodeType.SubProgramCall:
                        var subpgm = (SubProgramCall)code;
                        var dwg = drawingDict[subpgm.Id];
                        var part = new Part(dwg);
                        part.Rotate(Angle.ToRadians(subpgm.Rotation));
                        part.Offset(pos);
                        parts.Add(part);
                        break;
                }
            }

            return parts;
        }

        private int GetPlateId(string name)
        {
            return int.Parse(name.Replace("plate-", ""));
        }

        private int GetProgramId(string name)
        {
            return int.Parse(name.Replace("program-", ""));
        }

        public static T ParseEnum<T>(string value)
        {
            return (T)Enum.Parse(typeof(T), value, true);
        }

        public static bool TryParseEnum<T>(string value, out T e)
        {
            try
            {
                e = ParseEnum<T>(value);
                return true;
            }
            catch
            {
                e = ParseEnum<T>(typeof(T).GetEnumValues().GetValue(0).ToString());
            }

            return false;
        }

        private enum NestInfoSection
        {
            None,
            DefaultPlate,
            Material,
            EdgeSpacing,
            Source
        }
    }
}
