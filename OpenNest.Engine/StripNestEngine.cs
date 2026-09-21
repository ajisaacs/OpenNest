using System;
using System.Collections.Generic;
using System.Threading;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Geometry;

namespace OpenNest.Engine
{
    public class StripNestEngine : NestEngineBase
    {
        private StripPlateFiller filler;

        public StripNestEngine(Plate plate)
            : base(plate) { }

        public override string Name => "Strip";

        public override string Description =>
            "Iterative shrink-fill nesting for mixed-drawing layouts";

        private StripPlateFiller Filler
        {
            get
            {
                if (filler == null || !ReferenceEquals(filler.Plate, Plate))
                    filler = new LegacyStripPlateFiller(this, Plate);
                return filler;
            }
        }

        private StripPlateFiller PrepareFiller()
        {
            var current = Filler;
            current.PlateNumber = PlateNumber;
            current.NestDirection = NestDirection;
            return current;
        }

        public override List<Part> Fill(
            NestItem item,
            Box workArea,
            IProgress<NestProgress> progress,
            CancellationToken token
        )
        {
            var current = PrepareFiller();
            return current.Fill(item, workArea, progress, token);
        }

        public override List<Part> Fill(
            List<Part> groupParts,
            Box workArea,
            IProgress<NestProgress> progress,
            CancellationToken token
        )
        {
            var current = PrepareFiller();
            return current.Fill(groupParts, workArea, progress, token);
        }

        public override List<Part> PackArea(
            Box box,
            List<NestItem> items,
            IProgress<NestProgress> progress,
            CancellationToken token
        )
        {
            var current = PrepareFiller();
            return current.PackAreaCore(box, items, progress, token);
        }

        public override List<Part> Nest(
            List<NestItem> items,
            IProgress<NestProgress> progress,
            CancellationToken token
        )
        {
            var current = PrepareFiller();
            return current.Nest(items, progress, token);
        }

        private sealed class LegacyStripPlateFiller : StripPlateFiller
        {
            private readonly StripNestEngine engine;

            internal LegacyStripPlateFiller(StripNestEngine engine, Plate plate)
                : base(plate)
            {
                this.engine = engine;
            }

            public override List<Part> PackArea(
                Box box,
                List<NestItem> items,
                IProgress<NestProgress> progress,
                CancellationToken token
            ) => engine.PackArea(box, items, progress, token);
        }
    }
}
