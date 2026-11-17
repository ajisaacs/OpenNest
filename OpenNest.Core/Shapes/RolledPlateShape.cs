using System;
using System.Collections.Generic;
using System.ComponentModel;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    /// <summary>
    /// The flat blank of a plate rolled into a cylinder: <see cref="Length"/> by the
    /// circumference of the neutral axis, <see cref="KFactor"/> of the thickness out from the
    /// inside diameter.
    /// </summary>
    public class RolledPlateShape : ShapeDefinition
    {
        public double InsideDiameter { get; set; }
        public double Thickness { get; set; }

        [Unitless]
        [DisplayName("K-Factor")]
        public double KFactor { get; set; }

        public double Length { get; set; }

        public double InsideRadius => InsideDiameter / 2;

        public double OutsideDiameter => InsideDiameter + Thickness * 2;

        /// <summary>The developed width of the blank: the neutral axis's circumference.</summary>
        public double FlatLength => System.Math.PI * 2 * (InsideRadius + Thickness * KFactor);

        public override string GenerateName() =>
            $"Rolled Plate {Dim(InsideDiameter)} ID x {Dim(Thickness)} x {Dim(Length)}";

        public override void SetPreviewDefaults()
        {
            InsideDiameter = 10;
            Thickness = 0.1875;
            KFactor = 0.42;
            Length = 12;
        }

        public override IReadOnlyList<ShapeDimension> GetDimensions() =>
            new[]
            {
                ShapeDimension.Linear(nameof(Length), new Vector(0, 0), new Vector(Length, 0), ShapeDimension.Down),
            };

        /// <summary>The rolled ring, with its inside diameter across the middle and a leader from its wall to the thickness.</summary>
        public override ShapeSideView GetSideView()
        {
            var inside = InsideRadius;
            var outside = InsideRadius + Thickness;
            return new ShapeSideView(
                new Entity[] { new Circle(0, 0, outside), new Circle(0, 0, inside) },
                new[]
                {
                    ShapeDimension.Inline(nameof(InsideDiameter), new Vector(-inside, 0), new Vector(inside, 0), "Ø"),
                    ShapeDimension.Leader(nameof(Thickness), new Vector(outside * System.Math.Sqrt(0.5), outside * System.Math.Sqrt(0.5)), new Vector(1, 1)),
                });
        }

        public override Drawing GetDrawing()
        {
            SheetMetal.Validate(Thickness, Length, KFactor);
            if (!(InsideDiameter > 0))
                throw new InvalidOperationException("The inside diameter must be more than zero.");

            var entities = SheetMetal.Entities(Length, FlatLength, Array.Empty<double>(), 0, out _);
            return CreateDrawing(entities);
        }
    }
}
