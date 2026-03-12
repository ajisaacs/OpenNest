using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using OpenNest.CNC;
using OpenNest.Geometry;
using static OpenNest.IO.NestFormat;

namespace OpenNest.IO
{
    public sealed class NestReader
    {
        private readonly Stream stream;
        private readonly ZipArchive zipArchive;

        public NestReader(string file)
        {
            stream = new FileStream(file, FileMode.Open, FileAccess.Read);
            zipArchive = new ZipArchive(stream, ZipArchiveMode.Read);
        }

        public NestReader(Stream stream)
        {
            this.stream = stream;
            zipArchive = new ZipArchive(stream, ZipArchiveMode.Read);
        }

        public Nest Read()
        {
            var nestJson = ReadEntry("nest.json");
            var dto = JsonSerializer.Deserialize<NestDto>(nestJson, JsonOptions);

            var programs = ReadPrograms(dto.Drawings.Count);
            var drawingMap = BuildDrawings(dto, programs);
            var nest = BuildNest(dto, drawingMap);

            zipArchive.Dispose();
            stream.Close();

            return nest;
        }

        private string ReadEntry(string name)
        {
            var entry = zipArchive.GetEntry(name)
                ?? throw new InvalidDataException($"Nest file is missing required entry '{name}'.");
            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream);
            return reader.ReadToEnd();
        }

        private Dictionary<int, Program> ReadPrograms(int count)
        {
            var programs = new Dictionary<int, Program>();
            for (var i = 1; i <= count; i++)
            {
                var entry = zipArchive.GetEntry($"programs/program-{i}");
                if (entry == null) continue;

                using var entryStream = entry.Open();
                var memStream = new MemoryStream();
                entryStream.CopyTo(memStream);
                memStream.Position = 0;

                var reader = new ProgramReader(memStream);
                programs[i] = reader.Read();
            }
            return programs;
        }

        private Dictionary<int, Drawing> BuildDrawings(NestDto dto, Dictionary<int, Program> programs)
        {
            var map = new Dictionary<int, Drawing>();
            foreach (var d in dto.Drawings)
            {
                var drawing = new Drawing(d.Name);
                drawing.Customer = d.Customer;
                drawing.Color = Color.FromArgb(d.Color.A, d.Color.R, d.Color.G, d.Color.B);
                drawing.Quantity.Required = d.Quantity.Required;
                drawing.Priority = d.Priority;
                drawing.Constraints.StepAngle = d.Constraints.StepAngle;
                drawing.Constraints.StartAngle = d.Constraints.StartAngle;
                drawing.Constraints.EndAngle = d.Constraints.EndAngle;
                drawing.Constraints.Allow180Equivalent = d.Constraints.Allow180Equivalent;
                drawing.Material = new Material(d.Material.Name, d.Material.Grade, d.Material.Density);
                drawing.Source.Path = d.Source.Path;
                drawing.Source.Offset = new Vector(d.Source.Offset.X, d.Source.Offset.Y);

                if (programs.TryGetValue(d.Id, out var pgm))
                    drawing.Program = pgm;

                map[d.Id] = drawing;
            }
            return map;
        }

        private Nest BuildNest(NestDto dto, Dictionary<int, Drawing> drawingMap)
        {
            var nest = new Nest();
            nest.Name = dto.Name;

            Units units;
            if (Enum.TryParse(dto.Units, true, out units))
                nest.Units = units;

            nest.Customer = dto.Customer;
            nest.DateCreated = DateTime.Parse(dto.DateCreated);
            nest.DateLastModified = DateTime.Parse(dto.DateLastModified);
            nest.Notes = dto.Notes;

            // Plate defaults
            var pd = dto.PlateDefaults;
            nest.PlateDefaults.Size = new OpenNest.Geometry.Size(pd.Size.Width, pd.Size.Height);
            nest.PlateDefaults.Thickness = pd.Thickness;
            nest.PlateDefaults.Quadrant = pd.Quadrant;
            nest.PlateDefaults.PartSpacing = pd.PartSpacing;
            nest.PlateDefaults.Material = new Material(pd.Material.Name, pd.Material.Grade, pd.Material.Density);
            nest.PlateDefaults.EdgeSpacing = new Spacing(pd.EdgeSpacing.Left, pd.EdgeSpacing.Bottom, pd.EdgeSpacing.Right, pd.EdgeSpacing.Top);

            // Drawings
            foreach (var d in drawingMap.OrderBy(k => k.Key))
                nest.Drawings.Add(d.Value);

            // Plates
            foreach (var p in dto.Plates.OrderBy(p => p.Id))
            {
                var plate = new Plate();
                plate.Size = new OpenNest.Geometry.Size(p.Size.Width, p.Size.Height);
                plate.Thickness = p.Thickness;
                plate.Quadrant = p.Quadrant;
                plate.Quantity = p.Quantity;
                plate.PartSpacing = p.PartSpacing;
                plate.Material = new Material(p.Material.Name, p.Material.Grade, p.Material.Density);
                plate.EdgeSpacing = new Spacing(p.EdgeSpacing.Left, p.EdgeSpacing.Bottom, p.EdgeSpacing.Right, p.EdgeSpacing.Top);

                foreach (var partDto in p.Parts)
                {
                    if (!drawingMap.TryGetValue(partDto.DrawingId, out var dwg))
                        continue;

                    var part = new Part(dwg);
                    part.Rotate(partDto.Rotation);
                    part.Offset(new Vector(partDto.X, partDto.Y));
                    plate.Parts.Add(part);
                }

                nest.Plates.Add(plate);
            }

            return nest;
        }
    }
}
