using System;
using System.Collections.Generic;
using System.ComponentModel;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    /// <summary>
    /// The flat blank of a formed channel: two equal sides, a bottom and two 90° bends, sized
    /// from the outside dimensions with the bend allowance at <see cref="KFactor"/>.
    /// </summary>
    public class ChannelShape : ShapeDefinition
    {
        public double OutsideWidth { get; set; }
        public double OutsideHeight { get; set; }
        public double Thickness { get; set; }
        public double InsideRadius { get; set; }

        [Unitless]
        [DisplayName("K-Factor")]
        public double KFactor { get; set; }

        public double Length { get; set; }

        public double InsideWidth => OutsideWidth - Thickness * 2;

        public double InsideHeight => OutsideHeight - Thickness;

        public double BendAllowance => SheetMetal.BendAllowance(InsideRadius, Thickness, KFactor);

        /// <summary>The developed width of the blank: both sides' and the bottom's flats plus the bends.</summary>
        public double FlatLength =>
            (InsideHeight - InsideRadius) * 2 + InsideWidth - InsideRadius * 2 + BendAllowance * 2;

        /// <summary>Each bend line's distance from its side's edge.</summary>
        public double BendLineDim => InsideHeight - InsideRadius + BendAllowance / 2;

        public override string GenerateName() =>
            $"Channel {Dim(OutsideWidth)}x{Dim(OutsideHeight)}x{Dim(Thickness)} x {Dim(Length)}";

        public override void SetPreviewDefaults()
        {
            OutsideWidth = 4;
            OutsideHeight = 2;
            Thickness = 0.125;
            InsideRadius = 0.3125;
            KFactor = 0.42;
            Length = 12;
        }

        public override IReadOnlyList<ShapeDimension> GetDimensions() =>
            new[]
            {
                ShapeDimension.Linear(nameof(Length), new Vector(0, 0), new Vector(Length, 0), ShapeDimension.Down),
            };

        public override ShapeSideView GetSideView() =>
            SheetMetal.Section(true, OutsideWidth, OutsideHeight, Thickness, InsideRadius,
                nameof(OutsideWidth), nameof(OutsideHeight), nameof(Thickness), nameof(InsideRadius));

        public override Drawing GetDrawing()
        {
            SheetMetal.Validate(Thickness, Length, KFactor);
            if (!(InsideHeight - InsideRadius > 0))
                throw new InvalidOperationException("The sides must be longer than the thickness plus the inside radius.");
            if (InsideWidth - InsideRadius * 2 < 0)
                throw new InvalidOperationException("The width must fit both sides' thickness and inside radius.");

            var flat = FlatLength;
            var bendLine = BendLineDim;
            var entities = SheetMetal.Entities(Length, flat, new[] { bendLine, flat - bendLine }, InsideRadius, out var bends);
            var drawing = CreateDrawing(entities);
            drawing.Bends.AddRange(bends);
            return drawing;
        }
    }
}
