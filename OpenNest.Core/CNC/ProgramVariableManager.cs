using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenNest.CNC
{
    public sealed class ProgramVariableManager
    {
        private readonly Dictionary<int, ProgramVariable> _variables = new();

        public ProgramVariable GetOrCreate(string name, int number, string expression = null)
        {
            if (_variables.TryGetValue(number, out var existing))
                return existing;

            var variable = new ProgramVariable(number, name, expression);
            _variables[number] = variable;
            return variable;
        }

        public List<string> EmitDeclarations()
        {
            return _variables.Values
                .Where(v => v.Expression != null)
                .OrderBy(v => v.Number)
                .Select(v => $"{v.Reference}={v.Expression} ({FormatComment(v.Name)})")
                .ToList();
        }

        private static string FormatComment(string name)
        {
            // "LeadInFeedrate" -> "LEAD IN FEEDRATE"
            var sb = new StringBuilder();
            foreach (var c in name)
            {
                if (char.IsUpper(c) && sb.Length > 0)
                    sb.Append(' ');
                sb.Append(char.ToUpper(c));
            }
            return sb.ToString();
        }
    }
}
