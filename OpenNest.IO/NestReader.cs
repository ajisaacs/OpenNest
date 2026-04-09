using OpenNest.Bending;
using OpenNest.CNC;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
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
            var entitySets = ReadEntitySets(dto.Drawings.Count);
            var drawingMap = BuildDrawings(dto, programs, entitySets);
            ReadBestFits(drawingMap);
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

                // Read sub-programs if present
                var subsEntry = zipArchive.GetEntry($"programs/program-{i}-subs");
                if (subsEntry != null)
                {
                    using var subsStream = subsEntry.Open();
                    ReadSubPrograms(programs[i], subsStream);
                }
            }
            return programs;
        }

        private static void ReadSubPrograms(Program parent, Stream stream)
        {
            using var reader = new StreamReader(stream);
            var currentId = -1;
            var lines = new List<string>();

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith(":") && int.TryParse(trimmed.Substring(1), out var id))
                {
                    // Flush previous sub-program
                    if (currentId >= 0 && lines.Count > 0)
                        parent.SubPrograms[currentId] = ParseSubProgram(lines);

                    currentId = id;
                    lines.Clear();
                }
                else if (trimmed == "M99")
                {
                    if (currentId >= 0 && lines.Count > 0)
                        parent.SubPrograms[currentId] = ParseSubProgram(lines);

                    currentId = -1;
                    lines.Clear();
                }
                else
                {
                    lines.Add(trimmed);
                }
            }

            // Wire up SubProgramCall.Program references
            foreach (var code in parent.Codes)
            {
                if (code is SubProgramCall call && parent.SubPrograms.TryGetValue(call.Id, out var sub))
                    call.Program = sub;
            }
        }

        private static Program ParseSubProgram(List<string> lines)
        {
            var text = string.Join("\n", lines);
            var memStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
            var reader = new ProgramReader(memStream);
            return reader.Read();
        }

        private Dictionary<int, (List<Entity> entities, HashSet<Guid> suppressed)> ReadEntitySets(int count)
        {
            var result = new Dictionary<int, (List<Entity>, HashSet<Guid>)>();
            for (var i = 1; i <= count; i++)
            {
                var entry = zipArchive.GetEntry($"entities/entities-{i}");
                if (entry == null) continue;

                using var entryStream = entry.Open();
                using var reader = new StreamReader(entryStream);
                var json = reader.ReadToEnd();
                var dto = JsonSerializer.Deserialize<EntitySetDto>(json, JsonOptions);
                result[i] = EntitySerializer.FromDto(dto);
            }
            return result;
        }

        private Dictionary<int, Drawing> BuildDrawings(NestDto dto, Dictionary<int, Program> programs,
            Dictionary<int, (List<Entity> entities, HashSet<Guid> suppressed)> entitySets)
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

                if (d.Bends != null)
                {
                    foreach (var b in d.Bends)
                    {
                        drawing.Bends.Add(new Bend
                        {
                            StartPoint = new Vector(b.StartX, b.StartY),
                            EndPoint = new Vector(b.EndX, b.EndY),
                            Direction = Enum.TryParse<BendDirection>(b.Direction, true, out var dir)
                                ? dir : BendDirection.Unknown,
                            Angle = b.Angle,
                            Radius = b.Radius,
                            NoteText = b.NoteText
                        });
                    }
                }

                if (programs.TryGetValue(d.Id, out var pgm))
                    drawing.Program = pgm;

                if (entitySets.TryGetValue(d.Id, out var entitySet))
                {
                    drawing.SourceEntities = entitySet.entities;
                    drawing.SuppressedEntityIds = entitySet.suppressed;
                }

                map[d.Id] = drawing;
            }
            return map;
        }

        private void ReadBestFits(Dictionary<int, Drawing> drawingMap)
        {
            foreach (var kvp in drawingMap)
            {
                var entry = zipArchive.GetEntry($"bestfits/bestfit-{kvp.Key}");
                if (entry == null) continue;

                using var entryStream = entry.Open();
                using var reader = new StreamReader(entryStream);
                var json = reader.ReadToEnd();

                var sets = JsonSerializer.Deserialize<List<BestFitSetDto>>(json, JsonOptions);
                if (sets == null) continue;

                PopulateBestFitSets(kvp.Value, sets);
            }
        }

        private void PopulateBestFitSets(Drawing drawing, List<BestFitSetDto> sets)
        {
            foreach (var set in sets)
            {
                var results = set.Results.Select(r => new BestFitResult
                {
                    Candidate = new PairCandidate
                    {
                        Drawing = drawing,
                        Part1Rotation = r.Part1Rotation,
                        Part2Rotation = r.Part2Rotation,
                        Part2Offset = new Vector(r.Part2OffsetX, r.Part2OffsetY),
                        StrategyIndex = r.StrategyType,
                        TestNumber = r.TestNumber,
                        Spacing = r.CandidateSpacing
                    },
                    RotatedArea = r.RotatedArea,
                    BoundingWidth = r.BoundingWidth,
                    BoundingHeight = r.BoundingHeight,
                    OptimalRotation = r.OptimalRotation,
                    Keep = r.Keep,
                    Reason = r.Reason,
                    TrueArea = r.TrueArea,
                    HullAngles = r.HullAngles
                }).ToList();

                BestFitCache.Populate(drawing, set.PlateWidth, set.PlateHeight, set.Spacing, results);
            }
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
            nest.AssistGas = dto.AssistGas ?? "";

            // Nest-level material and thickness (fall back to PlateDefaults for old files)
            var pd = dto.PlateDefaults;
            var matDto = dto.Material ?? pd.Material;
            nest.Thickness = dto.Thickness > 0 ? dto.Thickness : pd.Thickness;
            nest.Material = new Material(matDto.Name, matDto.Grade, matDto.Density);

            // Plate defaults
            nest.PlateDefaults.Size = new OpenNest.Geometry.Size(pd.Size.Width, pd.Size.Length);
            nest.PlateDefaults.Quadrant = pd.Quadrant;
            nest.PlateDefaults.PartSpacing = pd.PartSpacing;
            nest.PlateDefaults.EdgeSpacing = new Spacing(pd.EdgeSpacing.Left, pd.EdgeSpacing.Bottom, pd.EdgeSpacing.Right, pd.EdgeSpacing.Top);

            // Plate optimizer settings
            nest.SalvageRate = dto.SalvageRate;
            if (dto.PlateOptions != null)
            {
                nest.PlateOptions = dto.PlateOptions.Select(o => new PlateOption
                {
                    Width = o.Width,
                    Length = o.Length,
                    Cost = o.Cost,
                }).ToList();
            }

            // Drawings
            foreach (var d in drawingMap.OrderBy(k => k.Key))
                nest.Drawings.Add(d.Value);

            // Plates
            foreach (var p in dto.Plates.OrderBy(p => p.Id))
            {
                var plate = new Plate();
                plate.Size = new OpenNest.Geometry.Size(p.Size.Width, p.Size.Length);
                plate.Quadrant = p.Quadrant;
                plate.Quantity = p.Quantity;
                plate.PartSpacing = p.PartSpacing;
                plate.EdgeSpacing = new Spacing(p.EdgeSpacing.Left, p.EdgeSpacing.Bottom, p.EdgeSpacing.Right, p.EdgeSpacing.Top);
                plate.GrainAngle = p.GrainAngle;

                foreach (var partDto in p.Parts)
                {
                    if (!drawingMap.TryGetValue(partDto.DrawingId, out var dwg))
                        continue;

                    var part = new Part(dwg);
                    part.Rotate(partDto.Rotation);
                    part.Offset(new Vector(partDto.X, partDto.Y));
                    plate.Parts.Add(part);
                }

                // Cut-offs
                if (p.CutOffs != null)
                {
                    foreach (var cutoffDto in p.CutOffs)
                    {
                        var axis = cutoffDto.Axis?.ToLowerInvariant() == "horizontal"
                            ? CutOffAxis.Horizontal
                            : CutOffAxis.Vertical;
                        var cutoff = new CutOff(new Vector(cutoffDto.X, cutoffDto.Y), axis)
                        {
                            StartLimit = cutoffDto.StartLimit,
                            EndLimit = cutoffDto.EndLimit
                        };
                        plate.CutOffs.Add(cutoff);
                    }

                    plate.RegenerateCutOffs(new CutOffSettings());
                }

                nest.Plates.Add(plate);
            }

            return nest;
        }
    }
}
