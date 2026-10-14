using System;
using System.Collections.Generic;
using OpenNest.Collections;
using OpenNest.Geometry;

namespace OpenNest
{
    public class Nest
    {
        public ObservableList<Plate> Plates;
        public DrawingCollection Drawings;

        public Nest()
            : this(string.Empty) { }

        public Nest(string name)
        {
            Name = name;
            Plates = new ObservableList<Plate>();
            Plates.ItemRemoved += Plates_PlateRemoved;
            Drawings = new DrawingCollection();
            PlateDefaults = new PlateSettings();
            Material = new Material();
            Customer = string.Empty;
            Notes = string.Empty;
        }

        private static void Plates_PlateRemoved(object sender, ItemRemovedEventArgs<Plate> e)
        {
            e.Item.Parts.Clear();
        }

        public string Name { get; set; }

        public string Customer { get; set; }

        public string Notes { get; set; }

        public string AssistGas { get; set; } = "";

        public NestStatus Status { get; set; } = NestStatus.Quote;

        public string MadeBy { get; set; } = "";

        public double Thickness { get; set; }

        public Material Material { get; set; }

        public Units Units { get; set; }

        public DateTime DateCreated { get; set; }

        public DateTime DateLastModified { get; set; }

        public PlateSettings PlateDefaults { get; set; }

        public List<PlateOption> PlateOptions { get; set; } = new();

        public double SalvageRate { get; set; } = 0.5;

        public Plate CreatePlate()
        {
            var plate = PlateDefaults.CreateNew();
            Plates.Add(plate);
            return plate;
        }

        /// <summary>
        /// Makes <paramref name="rail"/> this nest's machine clamp rail: new plates follow it
        /// (<see cref="PlateSettings.ClampRail"/>) and every existing plate re-resolves its
        /// zones from it. Null, or a rail without clamps, removes the rail and every plate's
        /// zones; the rail is nest-wide, so per-plate zones are replaced either way.
        /// </summary>
        public void SetClampRail(ClampRail rail)
        {
            if (rail != null && rail.Centers.Count == 0)
                rail = null;
            PlateDefaults.ClampRail = rail;
            foreach (var plate in Plates)
                plate.SetClampRail(rail);
        }

        public void UpdateDrawingQuantities()
        {
            foreach (var drawing in Drawings)
            {
                drawing.Quantity.Nested = 0;
            }

            foreach (var plate in Plates)
            {
                foreach (var part in plate.Parts)
                {
                    part.BaseDrawing.Quantity.Nested += plate.Quantity;
                }
            }
        }

        public class PlateSettings
        {
            private readonly Plate plate;

            public PlateSettings()
            {
                plate = new Plate();
            }

            public int Quadrant
            {
                get { return plate.Quadrant; }
                set { plate.Quadrant = value; }
            }

            public Size Size
            {
                get { return plate.Size; }
                set { plate.Size = value; }
            }

            public Spacing EdgeSpacing
            {
                get { return plate.EdgeSpacing; }
                set { plate.EdgeSpacing = value; }
            }

            public double PartSpacing
            {
                get { return plate.PartSpacing; }
                set { plate.PartSpacing = value; }
            }

            /// <summary>
            /// The machine clamp rail new plates follow, or null for none. Set through
            /// <see cref="Nest.SetClampRail"/> to also update existing plates.
            /// </summary>
            public ClampRail ClampRail { get; set; }

            public void SetFromExisting(Plate plate)
            {
                Quadrant = plate.Quadrant;
                Size = plate.Size;
                EdgeSpacing = plate.EdgeSpacing;
                PartSpacing = plate.PartSpacing;
            }

            public Plate CreateNew()
            {
                var created = new Plate()
                {
                    Size = Size,
                    EdgeSpacing = EdgeSpacing,
                    PartSpacing = PartSpacing,
                    Quadrant = Quadrant,
                    Quantity = 1,
                };
                if (ClampRail != null)
                    created.SetClampRail(ClampRail);
                return created;
            }
        }
    }
}
