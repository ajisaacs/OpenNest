using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.RapidPlanning;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Engine
{
    public class PlateProcessor
    {
        public IPartSequencer Sequencer { get; set; }
        public ContourCuttingStrategy CuttingStrategy { get; set; }
        public IRapidPlanner RapidPlanner { get; set; }

        public PlateProcessingResult Process(Plate plate)
        {
            var sequenced = Sequencer.Sequence(plate.Parts.ToList(), plate);
            var exitPoint = PlateHelper.GetExitPoint(plate);

            // Pass 1: process each part to collect pierce points
            var piercePoints = new Vector[sequenced.Count];
            var currentPoint = exitPoint;

            for (var i = 0; i < sequenced.Count; i++)
            {
                var part = sequenced[i].Part;

                if (!part.HasManualLeadIns && CuttingStrategy != null)
                {
                    var localApproach = ToPartLocal(currentPoint, part);
                    var result = CuttingStrategy.Apply(part.Program, localApproach);
                    piercePoints[i] = ToPlateSpace(GetProgramStartPoint(result.Program), part);
                    currentPoint = ToPlateSpace(result.LastCutPoint, part);
                }
                else
                {
                    piercePoints[i] = ToPlateSpace(GetProgramStartPoint(part.Program), part);
                    currentPoint = ToPlateSpace(GetProgramEndPoint(part.Program), part);
                }
            }

            // Pass 2: re-process with next part's start point for perimeter lead-in refinement
            var results = new List<ProcessedPart>(sequenced.Count);
            var cutAreas = new List<Shape>();
            currentPoint = exitPoint;

            for (var i = 0; i < sequenced.Count; i++)
            {
                var part = sequenced[i].Part;
                var localApproach = ToPartLocal(currentPoint, part);

                Program processedProgram;
                Vector lastCutLocal;

                if (!part.HasManualLeadIns && CuttingStrategy != null)
                {
                    CuttingResult cuttingResult;

                    if (i + 1 < sequenced.Count)
                    {
                        var nextStart = ToPartLocal(piercePoints[i + 1], part);
                        cuttingResult = CuttingStrategy.Apply(part.Program, localApproach, nextStart);
                    }
                    else
                    {
                        cuttingResult = CuttingStrategy.Apply(part.Program, localApproach);
                    }

                    processedProgram = cuttingResult.Program;
                    lastCutLocal = cuttingResult.LastCutPoint;
                }
                else
                {
                    processedProgram = part.Program;
                    lastCutLocal = GetProgramEndPoint(part.Program);
                }

                var pierceLocal = GetProgramStartPoint(processedProgram);
                var piercePoint = ToPlateSpace(pierceLocal, part);

                var rapidPath = RapidPlanner.Plan(currentPoint, piercePoint, cutAreas);

                results.Add(new ProcessedPart
                {
                    Part = part,
                    ProcessedProgram = processedProgram,
                    RapidPath = rapidPath
                });

                var perimeter = GetPartPerimeter(part);
                if (perimeter != null)
                    cutAreas.Add(perimeter);

                currentPoint = ToPlateSpace(lastCutLocal, part);
            }

            return new PlateProcessingResult { Parts = results };
        }

        private static Vector ToPartLocal(Vector platePoint, Part part)
        {
            return platePoint - part.Location;
        }

        private static Vector ToPlateSpace(Vector localPoint, Part part)
        {
            return localPoint + part.Location;
        }

        private static Vector GetProgramStartPoint(Program program)
        {
            if (program.Codes.Count == 0)
                return Vector.Zero;

            var first = program.Codes[0];
            if (first is Motion motion)
                return motion.EndPoint;

            return Vector.Zero;
        }

        private static Vector GetProgramEndPoint(Program program)
        {
            for (var i = program.Codes.Count - 1; i >= 0; i--)
            {
                if (program.Codes[i] is Motion motion)
                    return motion.EndPoint;
            }

            return Vector.Zero;
        }

        private static Shape GetPartPerimeter(Part part)
        {
            var entities = part.Program.ToGeometry();
            if (entities == null || entities.Count == 0)
                return null;

            var profile = new ShapeProfile(entities);
            var perimeter = profile.Perimeter;
            if (perimeter == null || perimeter.Entities.Count == 0)
                return null;

            perimeter.Offset(part.Location);
            return perimeter;
        }
    }
}
