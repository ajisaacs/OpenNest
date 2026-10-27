using System;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Math;
using SkiaSharp;

namespace OpenNest.Avalonia.Rendering
{
    /// <summary>
    /// A program's display geometry as Skia paths in the program frame, before placement.
    /// A Skia port of the desktop's GraphicsHelper.GetDisplayPaths, without tab bridging.
    /// </summary>
    internal sealed class ProgramPaths : IDisposable
    {
        public SKPath Cut { get; } = new() { FillType = SKPathFillType.EvenOdd };

        public SKPath Lead { get; } = new();

        public SKPath Etch { get; } = new();

        /// <summary>The material outline to fill: cuts and tabs, without open break cuts.</summary>
        public SKPath Fill { get; } = new() { FillType = SKPathFillType.EvenOdd };

        public SKRect Bounds { get; private set; }

        public static ProgramPaths Build(CNC.Program program)
        {
            var paths = new ProgramPaths();
            var position = Vector.Zero;
            paths.AddProgram(program, ref position);
            var bounds = paths.Cut.Bounds;
            if (!paths.Etch.IsEmpty)
                bounds = paths.Cut.IsEmpty ? paths.Etch.Bounds : SKRect.Union(bounds, paths.Etch.Bounds);
            paths.Bounds = bounds;
            return paths;
        }

        private SKPath TargetFor(LayerType layer) => layer switch
        {
            LayerType.Leadin or LayerType.Leadout => Lead,
            LayerType.Scribe => Etch,
            LayerType.Display or LayerType.Tab => null,
            _ => Cut,
        };

        private void AddProgram(CNC.Program program, ref Vector position)
        {
            // Sub-program offsets are relative to the frame origin at entry, not the tool position.
            var frameOrigin = position;
            var mode = program.Mode;
            SKPath open = null;

            for (var i = 0; i < program.Length; ++i)
            {
                var code = program[i];
                switch (code)
                {
                    case LinearMove line:
                        {
                            var end = mode == Mode.Incremental ? line.EndPoint + position : line.EndPoint;
                            var target = line.Suppressed ? null : TargetFor(line.Layer);
                            if (target != null)
                            {
                                Continue(ref open, target, position);
                                target.LineTo((float)end.X, (float)end.Y);
                            }
                            if (!line.Suppressed && line.Layer != LayerType.Break)
                                AddFillLine(line.Layer, position, end);
                            position = end;
                            break;
                        }

                    case ArcMove arc:
                        {
                            var end = arc.EndPoint;
                            var center = arc.CenterPoint;
                            if (mode == Mode.Incremental)
                            {
                                end += position;
                                center += position;
                            }
                            var target = arc.Suppressed ? null : TargetFor(arc.Layer);
                            if (target != null)
                            {
                                Continue(ref open, target, position);
                                AddArc(target, position, end, center, arc.Rotation);
                            }
                            if (!arc.Suppressed && arc.Layer != LayerType.Break)
                                AddFillArc(arc.Layer, position, end, center, arc.Rotation);
                            position = end;
                            break;
                        }

                    case RapidMove rapid:
                        {
                            var end = mode == Mode.Incremental ? rapid.EndPoint + position : rapid.EndPoint;
                            if (end.DistanceTo(position) > 0.001)
                                open = null;
                            position = end;
                            break;
                        }

                    case SubProgramCall call when call.Program != null:
                        open = null;
                        position = new Vector(frameOrigin.X + call.Offset.X, frameOrigin.Y + call.Offset.Y);
                        AddProgram(call.Program, ref position);
                        open = null;
                        break;
                }
            }
        }

        // Starts a new contour unless the move continues the one already open on the same path.
        private SKPath openFill;

        private static void Continue(ref SKPath open, SKPath target, Vector from)
        {
            if (!ReferenceEquals(open, target) || target.LastPoint != new SKPoint((float)from.X, (float)from.Y))
                target.MoveTo((float)from.X, (float)from.Y);
            open = target;
        }

        private bool IsMaterial(LayerType layer) =>
            layer is not (LayerType.Scribe or LayerType.Leadin or LayerType.Leadout or LayerType.Display);

        private void AddFillLine(LayerType layer, Vector from, Vector to)
        {
            if (!IsMaterial(layer))
                return;
            Continue(ref openFill, Fill, from);
            Fill.LineTo((float)to.X, (float)to.Y);
        }

        private void AddFillArc(LayerType layer, Vector from, Vector to, Vector center, RotationType rotation)
        {
            if (!IsMaterial(layer))
                return;
            Continue(ref openFill, Fill, from);
            AddArc(Fill, from, to, center, rotation);
        }

        private static void AddArc(SKPath path, Vector from, Vector to, Vector center, RotationType rotation)
        {
            var startAngle = Angle.NormalizeDeg(Angle.ToDegrees(System.Math.Atan2(from.Y - center.Y, from.X - center.X)));
            var endAngle = Angle.NormalizeDeg(Angle.ToDegrees(System.Math.Atan2(to.Y - center.Y, to.X - center.X)));
            var radius = (float)center.DistanceTo(to);
            var oval = new SKRect((float)center.X - radius, (float)center.Y - radius,
                (float)center.X + radius, (float)center.Y + radius);

            if (startAngle.IsEqualTo(endAngle))
            {
                // A full circle: two half sweeps keep the contour starting at the current point.
                var sweep = rotation == RotationType.CCW ? 180f : -180f;
                path.ArcTo(oval, (float)startAngle, sweep, false);
                path.ArcTo(oval, (float)startAngle + sweep, sweep, false);
                return;
            }

            if (rotation == RotationType.CCW && endAngle < startAngle)
                endAngle += 360.0;
            else if (rotation == RotationType.CW && startAngle < endAngle)
                startAngle += 360.0;

            path.ArcTo(oval, (float)startAngle, (float)(endAngle - startAngle), false);
        }

        public void Dispose()
        {
            Cut.Dispose();
            Lead.Dispose();
            Etch.Dispose();
            Fill.Dispose();
        }
    }
}
