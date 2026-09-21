using System;
using System.Collections.Generic;
using System.Threading;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;

namespace OpenNest.Engine
{
    public class DefaultNestEngine : NestEngineBase
    {
        private DefaultPlateFiller filler;
        private bool forceFullAngleSweep;

        public DefaultNestEngine(Plate plate)
            : base(plate) { }

        public override string Name => "Default";

        public override string Description =>
            "Multi-phase nesting (Linear, Pairs, RectBestFit, Extents)";

        public override NestPhase WinnerPhase
        {
            get => Filler.WinnerPhase;
            protected set => Filler.SetWinnerPhase(value);
        }

        public override List<PhaseResult> PhaseResults => Filler.PhaseResults;

        public override List<AngleResult> AngleResults => Filler.AngleResults;

        public bool ForceFullAngleSweep
        {
            get => forceFullAngleSweep;
            set
            {
                forceFullAngleSweep = value;
                if (filler != null)
                    filler.ForceFullAngleSweep = value;
            }
        }

        private DefaultPlateFiller Filler
        {
            get
            {
                if (filler == null || !ReferenceEquals(filler.Plate, Plate))
                {
                    filler = CreateFiller(Plate);
                    filler.ForceFullAngleSweep = forceFullAngleSweep;
                }
                return filler;
            }
        }

        private DefaultPlateFiller PrepareFiller()
        {
            var current = Filler;
            current.PlateNumber = PlateNumber;
            current.NestDirection = NestDirection;
            return current;
        }

        internal virtual DefaultPlateFiller CreateFiller(Plate plate) =>
            new LegacyDefaultPlateFiller(this, plate);

        internal DefaultPlateFiller CreateRemnantFiller(Plate plate, RemnantFillPolicy policy) =>
            new LegacyRemnantPlateFiller(this, plate, policy);

        protected override IFillComparer CreateComparer() => Filler.CreateComparerCore();

        public override NestDirection? PreferredDirection => null;

        public override ShrinkAxis TrimAxis => ShrinkAxis.Width;

        public override List<double> BuildAngles(
            NestItem item,
            ClassificationResult classification,
            Box workArea
        ) => Filler.BuildAnglesCore(item, classification, workArea);

        protected override void RecordProductiveAngles(List<AngleResult> angleResults) =>
            Filler.RecordProductiveAnglesCore(angleResults);

        protected virtual void RunPipeline(FillContext context) => Filler.RunPipelineCore(context);

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
            return current.PackArea(box, items, progress, token);
        }

        public override List<Part> Nest(
            List<NestItem> items,
            IProgress<NestProgress> progress,
            CancellationToken token
        )
        {
            return base.Nest(items, progress, token);
        }

        private sealed class LegacyDefaultPlateFiller : DefaultPlateFiller
        {
            private readonly DefaultNestEngine engine;

            internal LegacyDefaultPlateFiller(DefaultNestEngine engine, Plate plate)
                : base(plate)
            {
                this.engine = engine;
            }

            protected override IFillComparer CreateComparer() => engine.CreateComparer();

            public override NestDirection? PreferredDirection => engine.PreferredDirection;

            public override ShrinkAxis TrimAxis => engine.TrimAxis;

            public override List<double> BuildAngles(
                NestItem item,
                ClassificationResult classification,
                Box workArea
            ) => engine.BuildAngles(item, classification, workArea);

            protected override void RecordProductiveAngles(List<AngleResult> angleResults) =>
                engine.RecordProductiveAngles(angleResults);

            protected override void RunPipeline(FillContext context) => engine.RunPipeline(context);
        }

        private sealed class LegacyRemnantPlateFiller : RemnantPlateFiller
        {
            private readonly DefaultNestEngine engine;

            internal LegacyRemnantPlateFiller(
                DefaultNestEngine engine,
                Plate plate,
                RemnantFillPolicy policy
            )
                : base(plate, policy)
            {
                this.engine = engine;
            }

            protected override IFillComparer CreateComparer() => engine.CreateComparer();

            public override NestDirection? PreferredDirection => engine.PreferredDirection;

            public override ShrinkAxis TrimAxis => engine.TrimAxis;

            public override List<double> BuildAngles(
                NestItem item,
                ClassificationResult classification,
                Box workArea
            ) => engine.BuildAngles(item, classification, workArea);

            protected override void RecordProductiveAngles(List<AngleResult> angleResults) =>
                engine.RecordProductiveAngles(angleResults);

            protected override void RunPipeline(FillContext context) => engine.RunPipeline(context);
        }
    }
}
