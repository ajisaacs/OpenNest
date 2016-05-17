namespace OpenNest
{
    public class Material
    {
        public Material()
        {
        }

        public Material(string name)
        {
            Name = name;
        }

        public Material(string name, string grade)
        {
            Name = name;
            Grade = grade;
        }

        public Material(string name, string grade, double density)
        {
            Name = name;
            Grade = grade;
            Density = density;
        }

        public string Name { get; set; }

        public string Grade { get; set; }

        public double Density { get; set; }
    }
}
