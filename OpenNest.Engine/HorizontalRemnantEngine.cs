using System.Collections.Generic;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;

namespace OpenNest.Engine
{
    /// <summary>
    /// Optimizes for the largest top-side horizontal drop.
    /// </summary>
    public class HorizontalRemnantEngine : DefaultNestEngine
    {
        public HorizontalRemnantEngine(Plate plate)
            : base(plate) { }

        public override string Name => "Horizontal Remnant";

        public override string Description => "Optimizes for largest top-side horizontal drop";

        protected override IFillComparer CreateComparer() =>
            RemnantFillPolicy.Horizontal.CreateComparer();

        public override NestDirection? PreferredDirection =>
            RemnantFillPolicy.Horizontal.PreferredDirection;

        public override ShrinkAxis TrimAxis => RemnantFillPolicy.Horizontal.TrimAxis;

        public override List<double> BuildAngles(
            NestItem item,
            ClassificationResult classification,
            Box workArea
        ) => RemnantFillPolicy.Horizontal.BuildAngles(item, classification);

        internal override DefaultPlateFiller CreateFiller(Plate plate) =>
            CreateRemnantFiller(plate, RemnantFillPolicy.Horizontal);
    }
}
