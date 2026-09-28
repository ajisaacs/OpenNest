#nullable enable

using System;
using System.Collections.Generic;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Gpu
{
    public class GpuSlideComputer : ISlideComputer
    {
        private readonly Context _context;
        private readonly Accelerator _accelerator;
        private readonly object _lock = new object();

        // ── Kernels ──────────────────────────────────────────────────

        private readonly Action<
            Index1D,
            ArrayView1D<double, Stride1D.Dense>, // stationaryPrep
            ArrayView1D<double, Stride1D.Dense>, // movingPrep
            ArrayView1D<double, Stride1D.Dense>, // offsets
            ArrayView1D<ContactWitness, Stride1D.Dense>, // results
            int,
            int,
            int
        > _kernel;

        private readonly Action<
            Index1D,
            ArrayView1D<double, Stride1D.Dense>, // stationaryPrep
            ArrayView1D<double, Stride1D.Dense>, // movingPrep
            ArrayView1D<double, Stride1D.Dense>, // offsets
            ArrayView1D<ContactWitness, Stride1D.Dense>, // results
            ArrayView1D<int, Stride1D.Dense>, // directions
            int,
            int
        > _kernelMultiDir;

        private readonly Action<
            Index1D,
            ArrayView1D<double, Stride1D.Dense>, // raw
            ArrayView1D<double, Stride1D.Dense>, // prepared
            int
        > _prepareKernel;

        // ── Buffers ──────────────────────────────────────────────────

        private MemoryBuffer1D<double, Stride1D.Dense>? _gpuStationaryRaw;
        private MemoryBuffer1D<double, Stride1D.Dense>? _gpuStationaryPrep;
        private double[]? _lastStationaryData; // Active segment snapshot used for upload and contact topology

        private MemoryBuffer1D<double, Stride1D.Dense>? _gpuMovingRaw;
        private MemoryBuffer1D<double, Stride1D.Dense>? _gpuMovingPrep;
        private double[]? _lastMovingData; // Active segment snapshot used for upload and contact topology

        private MemoryBuffer1D<double, Stride1D.Dense>? _gpuOffsets;
        private MemoryBuffer1D<ContactWitness, Stride1D.Dense>? _gpuResults;
        private MemoryBuffer1D<int, Stride1D.Dense>? _gpuDirs;
        private int _offsetCapacity;

        public GpuSlideComputer() : this(preferCPU: false) { }

        public GpuSlideComputer(bool preferCPU)
        {
            _context = Context.CreateDefault();
            _accelerator = _context
                .GetPreferredDevice(preferCPU)
                .CreateAccelerator(_context);

            _kernel = _accelerator.LoadAutoGroupedStreamKernel<
                Index1D,
                ArrayView1D<double, Stride1D.Dense>,
                ArrayView1D<double, Stride1D.Dense>,
                ArrayView1D<double, Stride1D.Dense>,
                ArrayView1D<ContactWitness, Stride1D.Dense>,
                int,
                int,
                int
            >(SlideKernel);

            _kernelMultiDir = _accelerator.LoadAutoGroupedStreamKernel<
                Index1D,
                ArrayView1D<double, Stride1D.Dense>,
                ArrayView1D<double, Stride1D.Dense>,
                ArrayView1D<double, Stride1D.Dense>,
                ArrayView1D<ContactWitness, Stride1D.Dense>,
                ArrayView1D<int, Stride1D.Dense>,
                int,
                int
            >(SlideKernelMultiDir);

            _prepareKernel = _accelerator.LoadAutoGroupedStreamKernel<
                Index1D,
                ArrayView1D<double, Stride1D.Dense>,
                ArrayView1D<double, Stride1D.Dense>,
                int
            >(PrepareKernel);
        }

        public double[] ComputeBatch(
            double[] stationarySegments,
            int stationaryCount,
            double[] movingTemplateSegments,
            int movingCount,
            double[] offsets,
            int offsetCount,
            PushDirection direction
        )
        {
            var results = new double[offsetCount];
            if (offsetCount == 0 || stationaryCount == 0 || movingCount == 0)
            {
                Array.Fill(results, double.MaxValue);
                return results;
            }

            lock (_lock)
            {
                EnsureStationary(stationarySegments, stationaryCount);
                EnsureMoving(movingTemplateSegments, movingCount);
                EnsureOffsetBuffers(offsetCount);

                _gpuOffsets!.View.SubView(0, offsetCount * 2).CopyFromCPU(
                    _accelerator.DefaultStream, (ReadOnlySpan<double>)offsets.AsSpan(0, offsetCount * 2));

                _kernel(
                    offsetCount,
                    _gpuStationaryPrep!.View,
                    _gpuMovingPrep!.View,
                    _gpuOffsets.View.SubView(0, offsetCount * 2),
                    _gpuResults!.View.SubView(0, offsetCount),
                    stationaryCount,
                    movingCount,
                    (int)direction
                );

                _accelerator.Synchronize();
                var witnesses = new ContactWitness[offsetCount];
                _gpuResults.View.SubView(0, offsetCount).CopyToCPU(witnesses);
                ResolveContacts(witnesses, offsets, results, direction, null);
            }

            return results;
        }

        public double[] ComputeBatchMultiDir(
            double[] stationarySegments,
            int stationaryCount,
            double[] movingTemplateSegments,
            int movingCount,
            double[] offsets,
            int offsetCount,
            int[] directions
        )
        {
            var results = new double[offsetCount];
            if (offsetCount == 0 || stationaryCount == 0 || movingCount == 0)
            {
                Array.Fill(results, double.MaxValue);
                return results;
            }

            lock (_lock)
            {
                EnsureStationary(stationarySegments, stationaryCount);
                EnsureMoving(movingTemplateSegments, movingCount);
                EnsureOffsetBuffers(offsetCount);

                _gpuOffsets!.View.SubView(0, offsetCount * 2).CopyFromCPU(
                    _accelerator.DefaultStream, (ReadOnlySpan<double>)offsets.AsSpan(0, offsetCount * 2));
                _gpuDirs!.View.SubView(0, offsetCount).CopyFromCPU(
                    _accelerator.DefaultStream, (ReadOnlySpan<int>)directions.AsSpan(0, offsetCount));

                _kernelMultiDir(
                    offsetCount,
                    _gpuStationaryPrep!.View,
                    _gpuMovingPrep!.View,
                    _gpuOffsets.View.SubView(0, offsetCount * 2),
                    _gpuResults!.View.SubView(0, offsetCount),
                    _gpuDirs.View.SubView(0, offsetCount),
                    stationaryCount,
                    movingCount
                );

                _accelerator.Synchronize();
                var witnesses = new ContactWitness[offsetCount];
                _gpuResults.View.SubView(0, offsetCount).CopyToCPU(witnesses);
                ResolveContacts(witnesses, offsets, results, default, directions);
            }

            return results;
        }

        public void InvalidateStationary()
        {
            lock (_lock)
                _lastStationaryData = null;
        }

        public void InvalidateMoving()
        {
            lock (_lock)
                _lastMovingData = null;
        }

        private void ResolveContacts(
            ContactWitness[] witnesses,
            double[] offsets,
            double[] results,
            PushDirection direction,
            int[]? directions
        )
        {
            var moving = default(List<Line>);
            var stationary = default(List<Line>);
            var contacts = default(SlideContactClassifier);
            for (var i = 0; i < witnesses.Length; i++)
            {
                var witness = witnesses[i];
                results[i] = witness.Distance;
                if (witness.Distance == double.MaxValue)
                    continue;

                // GPU finds the nearest event. Prepare the complete boundaries only
                // once per batch, and share their material-side topology at each offset.
                if (contacts == null)
                {
                    moving = ToLines(_lastMovingData!);
                    stationary = ToLines(_lastStationaryData!);
                    contacts = SlideContactClassifier.FromLines(
                        moving, Vector.Zero, stationary, Vector.Zero).Prepare();
                }

                var offset = new Vector(offsets[i * 2], offsets[i * 2 + 1]);
                var push = directions == null ? direction : (PushDirection)directions[i];
                var unit = SpatialQuery.DirectionToOffset(push, 1);
                var placed = contacts.At(offset, Vector.Zero);
                if (placed.Blocks(
                    new Vector(witness.MovingX, witness.MovingY),
                    new Vector(witness.StationaryX, witness.StationaryY), unit.X, unit.Y))
                    continue;

                // A departing or grazing event does not discard the obstacle: replay
                // all events through the shared resolver to find the next blocking one,
                // including another contact tied at the same distance.
                results[i] = SpatialQuery.DirectionalDistance(
                    moving!, offset.X, offset.Y, stationary!, push, placed);
            }
        }

        private static List<Line> ToLines(double[] segments)
        {
            var lines = new List<Line>(segments.Length / 4);
            for (var i = 0; i < segments.Length; i += 4)
                lines.Add(new Line(segments[i], segments[i + 1], segments[i + 2], segments[i + 3]));
            return lines;
        }

        private void EnsureStationary(double[] data, int count)
        {
            // Cache the active prefix by value: callers may reuse an array with a
            // different segment count or mutate its coordinates between batches.
            var active = data.AsSpan(0, count * 4);
            if (_gpuStationaryPrep != null && _lastStationaryData != null
                && active.SequenceEqual(_lastStationaryData))
                return;

            _gpuStationaryRaw?.Dispose();
            _gpuStationaryPrep?.Dispose();

            var snapshot = active.ToArray();
            _gpuStationaryRaw = _accelerator.Allocate1D(snapshot);
            _gpuStationaryPrep = _accelerator.Allocate1D<double>(count * 10);

            _prepareKernel(count, _gpuStationaryRaw.View, _gpuStationaryPrep.View, count);
            _accelerator.Synchronize();

            _lastStationaryData = snapshot;
        }

        private void EnsureMoving(double[] data, int count)
        {
            // Cache the active prefix by value: callers may reuse an array with a
            // different segment count or mutate its coordinates between batches.
            var active = data.AsSpan(0, count * 4);
            if (_gpuMovingPrep != null && _lastMovingData != null
                && active.SequenceEqual(_lastMovingData))
                return;

            _gpuMovingRaw?.Dispose();
            _gpuMovingPrep?.Dispose();

            var snapshot = active.ToArray();
            _gpuMovingRaw = _accelerator.Allocate1D(snapshot);
            _gpuMovingPrep = _accelerator.Allocate1D<double>(count * 10);

            _prepareKernel(count, _gpuMovingRaw.View, _gpuMovingPrep.View, count);
            _accelerator.Synchronize();

            _lastMovingData = snapshot;
        }

        private void EnsureOffsetBuffers(int offsetCount)
        {
            if (_offsetCapacity >= offsetCount)
                return;

            var newCapacity = System.Math.Max(offsetCount, _offsetCapacity * 3 / 2);

            _gpuOffsets?.Dispose();
            _gpuResults?.Dispose();
            _gpuDirs?.Dispose();

            _gpuOffsets = _accelerator.Allocate1D<double>(newCapacity * 2);
            _gpuResults = _accelerator.Allocate1D<ContactWitness>(newCapacity);
            _gpuDirs = _accelerator.Allocate1D<int>(newCapacity);

            _offsetCapacity = newCapacity;
        }

        // ── Preparation Kernel ───────────────────────────────────────

        private static void PrepareKernel(
            Index1D index,
            ArrayView1D<double, Stride1D.Dense> raw,
            ArrayView1D<double, Stride1D.Dense> prepared,
            int count
        )
        {
            if (index >= count)
                return;
            var x1 = raw[index * 4 + 0];
            var y1 = raw[index * 4 + 1];
            var x2 = raw[index * 4 + 2];
            var y2 = raw[index * 4 + 3];

            prepared[index * 10 + 0] = x1;
            prepared[index * 10 + 1] = y1;
            prepared[index * 10 + 2] = x2;
            prepared[index * 10 + 3] = y2;

            var dx = x2 - x1;
            var dy = y2 - y1;

            // invD is used for parameter 't'. We use a small epsilon for stability.
            prepared[index * 10 + 4] = (XMath.Abs(dx) < Tolerance.Epsilon) ? 0 : 1.0 / dx;
            prepared[index * 10 + 5] = (XMath.Abs(dy) < Tolerance.Epsilon) ? 0 : 1.0 / dy;

            prepared[index * 10 + 6] = XMath.Min(x1, x2);
            prepared[index * 10 + 7] = XMath.Max(x1, x2);
            prepared[index * 10 + 8] = XMath.Min(y1, y2);
            prepared[index * 10 + 9] = XMath.Max(y1, y2);
        }

        // ── Main Slide Kernels ───────────────────────────────────────

        // Public because ILGPU's CPU backend emits kernel argument types in a separate assembly.
        public struct ContactWitness
        {
            public double Distance;
            public double MovingX;
            public double MovingY;
            public double StationaryX;
            public double StationaryY;
        }

        private static void Consider(
            ref ContactWitness nearest,
            double distance,
            double vx,
            double vy,
            int rayDirection,
            bool vertexMoves
        )
        {
            var snapped = distance > Tolerance.Epsilon ? distance : 0;
            if (snapped >= nearest.Distance)
                return;

            // Use the unsnapped hit for incidence; snapping a tiny gap to zero must
            // not move the witness off the other boundary.
            var dirX = rayDirection == 2 ? -1 : rayDirection == 3 ? 1 : 0;
            var dirY = rayDirection == 0 ? 1 : rayDirection == 1 ? -1 : 0;
            var hx = vx + distance * dirX;
            var hy = vy + distance * dirY;
            nearest.Distance = snapped;
            nearest.MovingX = vertexMoves ? vx : hx;
            nearest.MovingY = vertexMoves ? vy : hy;
            nearest.StationaryX = vertexMoves ? hx : vx;
            nearest.StationaryY = vertexMoves ? hy : vy;
        }

        private static void SlideKernel(
            Index1D index,
            ArrayView1D<double, Stride1D.Dense> stationaryPrep,
            ArrayView1D<double, Stride1D.Dense> movingPrep,
            ArrayView1D<double, Stride1D.Dense> offsets,
            ArrayView1D<ContactWitness, Stride1D.Dense> results,
            int sCount,
            int mCount,
            int direction
        )
        {
            if (index >= results.Length)
                return;

            var dx = offsets[index * 2];
            var dy = offsets[index * 2 + 1];

            results[index] = ComputeSlideLean(
                stationaryPrep,
                movingPrep,
                dx,
                dy,
                sCount,
                mCount,
                direction
            );
        }

        private static void SlideKernelMultiDir(
            Index1D index,
            ArrayView1D<double, Stride1D.Dense> stationaryPrep,
            ArrayView1D<double, Stride1D.Dense> movingPrep,
            ArrayView1D<double, Stride1D.Dense> offsets,
            ArrayView1D<ContactWitness, Stride1D.Dense> results,
            ArrayView1D<int, Stride1D.Dense> directions,
            int sCount,
            int mCount
        )
        {
            if (index >= results.Length)
                return;

            var dx = offsets[index * 2];
            var dy = offsets[index * 2 + 1];
            var dir = directions[index];

            results[index] = ComputeSlideLean(
                stationaryPrep,
                movingPrep,
                dx,
                dy,
                sCount,
                mCount,
                dir
            );
        }

        private static ContactWitness ComputeSlideLean(
            ArrayView1D<double, Stride1D.Dense> sPrep,
            ArrayView1D<double, Stride1D.Dense> mPrep,
            double dx,
            double dy,
            int sCount,
            int mCount,
            int direction
        )
        {
            const double eps = Tolerance.Epsilon;
            var nearest = new ContactWitness { Distance = double.MaxValue };
            var horizontal = direction >= 2;
            var oppDir = direction ^ 1;

            // ── Forward Pass: moving vertices vs stationary edges ─────
            for (var i = 0; i < mCount; i++)
            {
                var m1x = mPrep[i * 10 + 0] + dx;
                var m1y = mPrep[i * 10 + 1] + dy;
                var m2x = mPrep[i * 10 + 2] + dx;
                var m2y = mPrep[i * 10 + 3] + dy;

                for (var j = 0; j < sCount; j++)
                {
                    var sMin = horizontal ? sPrep[j * 10 + 8] : sPrep[j * 10 + 6];
                    var sMax = horizontal ? sPrep[j * 10 + 9] : sPrep[j * 10 + 7];

                    // Test moving vertex 1 against stationary edge j
                    var mv1 = horizontal ? m1y : m1x;
                    if (mv1 >= sMin - eps && mv1 <= sMax + eps)
                    {
                        var d = RayEdgeLean(m1x, m1y, sPrep, j, direction, eps);
                        Consider(ref nearest, d, m1x, m1y,
                            direction, vertexMoves: true);
                    }

                    // Test moving vertex 2 against stationary edge j
                    var mv2 = horizontal ? m2y : m2x;
                    if (mv2 >= sMin - eps && mv2 <= sMax + eps)
                    {
                        var d = RayEdgeLean(m2x, m2y, sPrep, j, direction, eps);
                        Consider(ref nearest, d, m2x, m2y,
                            direction, vertexMoves: true);
                    }
                }
            }

            // ── Reverse Pass: stationary vertices vs moving edges ─────
            for (var i = 0; i < sCount; i++)
            {
                var s1x = sPrep[i * 10 + 0];
                var s1y = sPrep[i * 10 + 1];
                var s2x = sPrep[i * 10 + 2];
                var s2y = sPrep[i * 10 + 3];

                for (var j = 0; j < mCount; j++)
                {
                    var mMin = horizontal ? (mPrep[j * 10 + 8] + dy) : (mPrep[j * 10 + 6] + dx);
                    var mMax = horizontal ? (mPrep[j * 10 + 9] + dy) : (mPrep[j * 10 + 7] + dx);

                    // Test stationary vertex 1 against moving edge j
                    var sv1 = horizontal ? s1y : s1x;
                    if (sv1 >= mMin - eps && sv1 <= mMax + eps)
                    {
                        var d = RayEdgeLeanMoving(s1x, s1y, mPrep, j, dx, dy, oppDir, eps);
                        Consider(ref nearest, d, s1x, s1y,
                            oppDir, vertexMoves: false);
                    }

                    // Test stationary vertex 2 against moving edge j
                    var sv2 = horizontal ? s2y : s2x;
                    if (sv2 >= mMin - eps && sv2 <= mMax + eps)
                    {
                        var d = RayEdgeLeanMoving(s2x, s2y, mPrep, j, dx, dy, oppDir, eps);
                        Consider(ref nearest, d, s2x, s2y,
                            oppDir, vertexMoves: false);
                    }
                }
            }

            return nearest;
        }

        private static double RayEdgeLean(
            double vx,
            double vy,
            ArrayView1D<double, Stride1D.Dense> sPrep,
            int j,
            int direction,
            double eps
        )
        {
            var p1x = sPrep[j * 10 + 0];
            var p1y = sPrep[j * 10 + 1];
            var p2x = sPrep[j * 10 + 2];
            var p2y = sPrep[j * 10 + 3];

            if (direction >= 2) // Horizontal (Left=2, Right=3)
            {
                var invDy = sPrep[j * 10 + 5];
                if (invDy == 0)
                    return double.MaxValue;

                var t = (vy - p1y) * invDy;
                if (t < -eps || t > 1.0 + eps)
                    return double.MaxValue;

                var ix = p1x + t * (p2x - p1x);
                var dist = (direction == 2) ? (vx - ix) : (ix - vx);

                return dist >= -eps ? dist : double.MaxValue;
            }
            else // Vertical (Up=0, Down=1)
            {
                var invDx = sPrep[j * 10 + 4];
                if (invDx == 0)
                    return double.MaxValue;

                var t = (vx - p1x) * invDx;
                if (t < -eps || t > 1.0 + eps)
                    return double.MaxValue;

                var iy = p1y + t * (p2y - p1y);
                var dist = (direction == 1) ? (vy - iy) : (iy - vy);

                return dist >= -eps ? dist : double.MaxValue;
            }
        }

        private static double RayEdgeLeanMoving(
            double vx,
            double vy,
            ArrayView1D<double, Stride1D.Dense> mPrep,
            int j,
            double dx,
            double dy,
            int direction,
            double eps
        )
        {
            var p1x = mPrep[j * 10 + 0] + dx;
            var p1y = mPrep[j * 10 + 1] + dy;
            var p2x = mPrep[j * 10 + 2] + dx;
            var p2y = mPrep[j * 10 + 3] + dy;

            if (direction >= 2) // Horizontal
            {
                var invDy = mPrep[j * 10 + 5];
                if (invDy == 0)
                    return double.MaxValue;

                var t = (vy - p1y) * invDy;
                if (t < -eps || t > 1.0 + eps)
                    return double.MaxValue;

                var ix = p1x + t * (p2x - p1x);
                var dist = (direction == 2) ? (vx - ix) : (ix - vx);

                return dist >= -eps ? dist : double.MaxValue;
            }
            else // Vertical
            {
                var invDx = mPrep[j * 10 + 4];
                if (invDx == 0)
                    return double.MaxValue;

                var t = (vx - p1x) * invDx;
                if (t < -eps || t > 1.0 + eps)
                    return double.MaxValue;

                var iy = p1y + t * (p2y - p1y);
                var dist = (direction == 1) ? (vy - iy) : (iy - vy);

                return dist >= -eps ? dist : double.MaxValue;
            }
        }

        public void Dispose()
        {
            _gpuStationaryRaw?.Dispose();
            _gpuStationaryPrep?.Dispose();
            _gpuMovingRaw?.Dispose();
            _gpuMovingPrep?.Dispose();
            _gpuOffsets?.Dispose();
            _gpuResults?.Dispose();
            _gpuDirs?.Dispose();
            _accelerator?.Dispose();
            _context?.Dispose();
        }
    }
}
