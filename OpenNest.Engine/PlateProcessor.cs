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

        public PlateResult Process(Plate plate)
        {
            var sequenced = Sequencer.Sequence(plate.Parts.ToList(), plate);
            var results = new List<ProcessedPart>(sequenced.Count);
            var cutAreas = new List<Shape>();
            var currentPoint = PlateHelper.GetExitPoint(plate);

            foreach (var sp in sequenced)
            {
                var part = sp.Part;

                // Compute approach point in part-local space
                var localApproach = ToPartLocal(currentPoint, part);

                Program processedProgram;
                Vector lastCutLocal;

                if (!part.HasManualLeadIns && CuttingStrategy != null)
                {
                    var cuttingResult = CuttingStrategy.Apply(part.Program, localApproach);
                    processedProgram = cuttingResult.Program;
                    lastCutLocal = cuttingResult.LastCutPoint;
                }
                else
                {
                    processedProgram = part.Program;
                    lastCutLocal = GetProgramEndPoint(part.Program);
                }

                // Pierce point: program start point in plate space
                var pierceLocal = GetProgramStartPoint(processedProgram);
                var piercePoint = ToPlateSpace(pierceLocal, part);

                // Plan rapid from currentPoint to pierce point
                var rapidPath = RapidPlanner.Plan(currentPoint, piercePoint, cutAreas);

                results.Add(new ProcessedPart
                {
                    Part = part,
                    ProcessedProgram = processedProgram,
                    RapidPath = rapidPath
                });

                // Update cut areas with part perimeter
                var perimeter = GetPartPerimeter(part);
                if (perimeter != null)
                    cutAreas.Add(perimeter);

                // Update current point to last cut point in plate space
                currentPoint = ToPlateSpace(lastCutLocal, part);
            }

            return new PlateResult { Parts = results };
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
