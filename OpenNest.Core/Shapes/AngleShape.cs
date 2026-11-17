using System;
using System.Collections.Generic;
using System.ComponentModel;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    /// <summary>
    /// The flat blank of a formed angle: two legs and one 90° bend, sized from the outside
    /// dimensions with the bend allowance at <see cref="KFactor"/>.
    /// </summary>
    public class AngleShape : ShapeDefinition
    {
        public double OutsideWidth { get; set; }
        public double OutsideHeight { get; set; }
        public double Thickness { get; set; }
        public double InsideRadius { get; set; }

        [Unitless]
        [DisplayName("K-Factor")]
        public double KFactor { get; set; }

        public double Length { get; set; }

        public double InsideWidth => OutsideWidth - Thickness;

        public double InsideHeight => OutsideHeight - Thickness;

        public double BendAllowance => SheetMetal.BendAllowance(InsideRadius, Thickness, KFactor);

        /// <summary>The developed width of the blank: both legs' flats plus the bend.</summary>
        public double FlatLength => InsideHeight - InsideRadius + InsideWidth - InsideRadius + BendAllowance;

        /// <summary>The bend line's distance from the edge of the shorter leg.</summary>
        public double BendLineDim => System.Math.Min(InsideWidth, InsideHeight) - InsideRadius + BendAllowance / 2;

        public override string GenerateName() =>
            $"Angle {Dim(OutsideWidth)}x{Dim(OutsideHeight)}x{Dim(Thickness)} x {Dim(Length)}";

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
            SheetMetal.Section(false, OutsideWidth, OutsideHeight, Thickness, InsideRadius,
                nameof(OutsideWidth), nameof(OutsideHeight), nameof(Thickness), nameof(InsideRadius));

        public override Drawing GetDrawing()
        {
            SheetMetal.Validate(Thickness, Length, KFactor);
            if (!(InsideHeight - InsideRadius > 0) || !(InsideWidth - InsideRadius > 0))
                throw new InvalidOperationException("Each leg must be longer than the thickness plus the inside radius.");

            // The shorter leg runs along the bottom edge, so the bend line sits BendLineDim up.
            var entities = SheetMetal.Entities(Length, FlatLength, new[] { BendLineDim }, InsideRadius, out var bends);
            var drawing = CreateDrawing(entities);
            drawing.Bends.AddRange(bends);
            return drawing;
        }
    }
}
