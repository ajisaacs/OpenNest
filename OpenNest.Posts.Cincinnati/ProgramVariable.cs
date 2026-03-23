namespace OpenNest.Posts.Cincinnati
{
    public sealed class ProgramVariable
    {
        public int Number { get; }
        public string Name { get; }
        public string Expression { get; set; }

        public ProgramVariable(int number, string name, string expression = null)
        {
            Number = number;
            Name = name;
            Expression = expression;
        }

        public string Reference => $"#{Number}";
    }
}
