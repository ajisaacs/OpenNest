using OpenNest.Engine.Fill;
using OpenNest.Engine.Nfp;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenNest
{
    public class NfpNestEngine : NestEngineBase
    {
        public NfpNestEngine(Plate plate) : base(plate)
        {
        }

        public override string Name => "NFP";

        public override string Description => "NFP-based mixed-part nesting with simulated annealing";

        public override List<Part> Fill(NestItem item, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            var inner = new DefaultNestEngine(Plate);
            return inner.Fill(item, workArea, progress, token);
        }

        public override List<Part> Fill(List<Part> groupParts, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            var inner = new DefaultNestEngine(Plate);
            return inner.Fill(groupParts, workArea, progress, token);
        }

        public override List<Part> PackArea(Box box, List<NestItem> items,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            var inner = new DefaultNestEngine(Plate);
            return inner.PackArea(box, items, progress, token);
        }

        public override List<Part> Nest(List<NestItem> items,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            if (items == null || items.Count == 0)
                return new List<Part>();

            var parts = AutoNester.Nest(items, Plate, progress, token);

            // Compact placed parts toward the origin to close gaps.
            Compactor.Settle(parts, Plate.WorkArea(), Plate.PartSpacing);

            // Deduct placed quantities from original items.
            foreach (var item in items)
            {
                if (item.Quantity <= 0)
                    continue;

                var placed = parts.FindAll(p => p.BaseDrawing.Name == item.Drawing.Name).Count;
                item.Quantity = System.Math.Max(0, item.Quantity - placed);
            }

            return parts;
        }
    }
}
