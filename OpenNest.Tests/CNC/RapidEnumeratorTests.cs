using OpenNest.CNC;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Tests.CNC
{
    public class RapidEnumeratorTests
    {
        [Fact]
        public void Enumerate_AbsoluteProgram_OffsetsMotionsByBasePos()
        {
            var pgm = new Program(Mode.Absolute);
            pgm.Codes.Add(new RapidMove(1, 0));
            pgm.Codes.Add(new LinearMove(2, 0));
            pgm.Codes.Add(new RapidMove(3, 3));

            var segments = RapidEnumerator.Enumerate(pgm, basePos: new Vector(100, 200), startPos: new Vector(0, 0));

            // Origin → first pierce, then interior rapid from contour end to next rapid target.
            Assert.Equal(2, segments.Count);
            Assert.Equal(new Vector(0, 0), segments[0].From);
            Assert.Equal(new Vector(101, 200), segments[0].To);
            Assert.Equal(new Vector(102, 200), segments[1].From);
            Assert.Equal(new Vector(103, 203), segments[1].To);
        }

        [Fact]
        public void Enumerate_IncrementalProgram_InterpretsDeltasFromBasePos()
        {
            // Pre-lead-in raw program: first rapid normalized to (0,0), Mode=Incremental
            // (matches ConvertGeometry.ToProgram output).
            var pgm = new Program(Mode.Incremental);
            pgm.Codes.Add(new RapidMove(0, 0));
            pgm.Codes.Add(new LinearMove(5, 0));
            pgm.Codes.Add(new LinearMove(0, 5));
            pgm.Codes.Add(new RapidMove(1, 1));

            var segments = RapidEnumerator.Enumerate(pgm, basePos: new Vector(100, 200), startPos: new Vector(0, 0));

            Assert.Equal(2, segments.Count);
            // First rapid: plate origin → part pierce at basePos.
            Assert.Equal(new Vector(0, 0), segments[0].From);
            Assert.Equal(new Vector(100, 200), segments[0].To);
            // Interior rapid: after deltas (5,0) and (0,5) from basePos, rapid delta (1,1).
            Assert.Equal(new Vector(105, 205), segments[1].From);
            Assert.Equal(new Vector(106, 206), segments[1].To);
        }

        [Fact]
        public void Enumerate_SubProgramCall_RapidEndsAtAbsoluteHolePierce()
        {
            // Main program: lead-in rapid, a line, then a SubProgramCall for a hole.
            // Sub-program (incremental) starts with RapidMove(radius, 0) to the hole pierce.
            var sub = new Program(Mode.Incremental);
            sub.Codes.Add(new RapidMove(0.5, 0));
            sub.Codes.Add(new LinearMove(0, 0.1));

            var pgm = new Program(Mode.Absolute);
            pgm.Codes.Add(new RapidMove(0.2, 0.3));             // first pierce (perimeter lead-in)
            pgm.Codes.Add(new LinearMove(1.0, 1.0));             // contour move
            pgm.Codes.Add(new SubProgramCall
            {
                Id = 1,
                Program = sub,
                Offset = new Vector(2, 2),                       // hole center (drawing-local)
            });

            var basePos = new Vector(100, 200);                  // part.Location
            var segments = RapidEnumerator.Enumerate(pgm, basePos, startPos: new Vector(0, 0));

            // Expected rapids:
            //  1. origin → first pierce (0.2+100, 0.3+200) = (100.2, 200.3)
            //  2. end of contour (1+100, 1+200) = (101, 201) → hole pierce (2+100+0.5, 2+200) = (102.5, 202)
            //  The sub's internal first rapid is skipped (already drawn in #2).
            Assert.Equal(2, segments.Count);

            Assert.Equal(new Vector(0, 0), segments[0].From);
            Assert.Equal(new Vector(100.2, 200.3), segments[0].To);

            Assert.Equal(new Vector(101, 201), segments[1].From);
            Assert.Equal(new Vector(102.5, 202), segments[1].To);
        }
    }
}
