using OpenNest.Collections;
using OpenNest.Geometry;
using System;

namespace OpenNest
{
    public class Nest
    {
        public ObservableList<Plate> Plates;
        public DrawingCollection Drawings;

        public Nest()
            : this(string.Empty)
        {
        }

        public Nest(string name)
        {
            Name = name;
            Plates = new ObservableList<Plate>();
            Plates.ItemRemoved += Plates_PlateRemoved;
            Drawings = new DrawingCollection();
            PlateDefaults = new PlateSettings();
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

        public Units Units { get; set; }

        public DateTime DateCreated { get; set; }

        public DateTime DateLastModified { get; set; }

        public PlateSettings PlateDefaults { get; set; }

        public Plate CreatePlate()
        {
            var plate = PlateDefaults.CreateNew();
            Plates.Add(plate);
            return plate;
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

            public double Thickness
            {
                get { return plate.Thickness; }
                set { plate.Thickness = value; }
            }

            public Material Material
            {
                get { return plate.Material; }
                set { plate.Material = value; }
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

            public void SetFromExisting(Plate plate)
            {
                Thickness = plate.Thickness;
                Quadrant = plate.Quadrant;
                Material = plate.Material;
                Size = plate.Size;
                EdgeSpacing = plate.EdgeSpacing;
                PartSpacing = plate.PartSpacing;
            }

            public Plate CreateNew()
            {
                return new Plate()
                {
                    Thickness = Thickness,
                    Size = Size,
                    EdgeSpacing = EdgeSpacing,
                    PartSpacing = PartSpacing,
                    Material = Material,
                    Quadrant = Quadrant,
                    Quantity = 1
                };
            }
        }
    }
}
