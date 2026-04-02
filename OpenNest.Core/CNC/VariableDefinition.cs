namespace OpenNest.CNC
{
    public sealed class VariableDefinition
    {
        public string Name { get; }
        public string Expression { get; }
        public double Value { get; }
        public bool Inline { get; }
        public bool Global { get; }

        public VariableDefinition(string name, string expression, double value,
            bool inline = false, bool global = false)
        {
            Name = name;
            Expression = expression;
            Value = value;
            Inline = inline;
            Global = global;
        }
    }
}
