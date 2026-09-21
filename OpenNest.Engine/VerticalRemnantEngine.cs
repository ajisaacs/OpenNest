using System.Collections.Generic;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;

namespace OpenNest.Engine
{
    /// <summary>
    /// Optimizes for the largest right-side vertical drop.
    /// </summary>
    public class VerticalRemnantEngine : DefaultNestEngine
    {
        public VerticalRemnantEngine(Plate plate)
            : base(plate) { }

        public override string Name => "Vertical Remnant";

        public override string Description => "Optimizes for largest right-side vertical drop";

        protected override IFillComparer CreateComparer() => RemnantFillPolicy.Vertical.CreateComparer();

        public override NestDirection? PreferredDirection => RemnantFillPolicy.Vertical.PreferredDirection;

        public override List<double> BuildAngles(
            NestItem item,
            ClassificationResult classification,
            Box workArea
        ) => RemnantFillPolicy.Vertical.BuildAngles(item, classification);

        internal override DefaultPlateFiller CreateFiller(Plate plate) =>
            CreateRemnantFiller(plate, RemnantFillPolicy.Vertical);
    }
}
