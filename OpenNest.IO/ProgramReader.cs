using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Math;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenNest.IO
{
    public sealed class ProgramReader
    {
        private const int BufferSize = 200;

        private int codeIndex;
        private CodeBlock block;
        private CodeSection section;
        private Program program;
        private StreamReader reader;
        private Dictionary<string, double> resolvedVariables;

        public ProgramReader(Stream stream)
        {
            reader = new StreamReader(stream);
            program = new Program();
        }

        public Program Read()
        {
            // First pass: read all lines, collect variable definitions
            var allLines = new List<string>();
            var variableDefs = new Dictionary<string, (string expression, bool inline, bool global)>(
                StringComparer.OrdinalIgnoreCase);
            var codeLines = new List<string>();
            string line;

            while ((line = reader.ReadLine()) != null)
            {
                allLines.Add(line);
                if (TryParseVariableDefinition(line, out var name, out var expression, out var isInline, out var isGlobal))
                    variableDefs[name] = (expression, isInline, isGlobal);
                else
                    codeLines.Add(line);
            }

            // Evaluate variables with topological sort for dependency ordering
            resolvedVariables = ResolveVariables(variableDefs);

            // Store evaluated variables on the program
            foreach (var kvp in variableDefs)
            {
                var name = kvp.Key;
                var (expression, isInline, isGlobal) = kvp.Value;
                var value = resolvedVariables[name];
                program.Variables[name] = new VariableDefinition(name, expression, value, isInline, isGlobal);
            }

            // Second pass: parse G-code lines with variable substitution
            foreach (var codeLine in codeLines)
            {
                block = ParseBlock(codeLine);
                ProcessCurrentBlock();
            }

            return program;
        }

        private CodeBlock ParseBlock(string line)
        {
            var block = new CodeBlock();
            Code code = null;
            for (var i = 0; i < line.Length; ++i)
            {
                var c = line[i];
                if (c == '$' && code != null && resolvedVariables != null)
                {
                    // Read the maximal variable name (letters, digits, underscores)
                    var start = i + 1;
                    while (start < line.Length && (char.IsLetterOrDigit(line[start]) || line[start] == '_'))
                        start++;
                    var maxName = line.Substring(i + 1, start - i - 1);

                    // Try longest match first, then progressively shorter to handle
                    // cases like X$widthY0 where "widthY" isn't a variable but "width" is
                    string lookupKey = null;
                    var nameLen = maxName.Length;
                    while (nameLen > 0)
                    {
                        var candidate = maxName.Substring(0, nameLen);
                        lookupKey = resolvedVariables.Keys
                            .FirstOrDefault(k => string.Equals(k, candidate, StringComparison.OrdinalIgnoreCase));
                        if (lookupKey != null)
                            break;
                        nameLen--;
                    }

                    if (lookupKey != null)
                    {
                        code.Value = resolvedVariables[lookupKey].ToString(CultureInfo.InvariantCulture);
                        code.VariableRef = lookupKey;
                        i += nameLen; // advance past the matched variable name
                    }
                    else
                    {
                        i = start - 1; // no match, skip the whole thing
                    }
                }
                else if (char.IsLetter(c))
                    block.Add((code = new Code(c)));
                else if (c == ':')
                {
                    block.Add((new Code(c, line.Remove(0, i + 1).Trim())));
                    break;
                }
                else if (code != null)
                    code.Value += c;
            }
            return block;
        }

        private void ProcessCurrentBlock()
        {
            var code = GetFirstCode();

            while (code != null)
            {
                switch (code.Id)
                {
                    case ':':
                        program.Codes.Add(new Comment(code.Value));
                        code = GetNextCode();
                        break;

                    case 'G':
                        int value = int.Parse(code.Value);

                        switch (value)
                        {
                            case 0:
                            case 1:
                                section = CodeSection.Line;
                                ReadLine(value == 0);
                                code = GetCurrentCode();
                                break;

                            case 2:
                            case 3:
                                section = CodeSection.Arc;
                                ReadArc(value == 2 ? RotationType.CW : RotationType.CCW);
                                code = GetCurrentCode();
                                break;

                            case 65:
                                section = CodeSection.SubProgram;
                                ReadSubProgram();
                                code = GetCurrentCode();
                                break;

                            case 40:
                                program.Codes.Add(new Kerf() { Value = KerfType.None });
                                code = GetNextCode();
                                break;

                            case 41:
                                program.Codes.Add(new Kerf() { Value = KerfType.Left });
                                code = GetNextCode();
                                break;

                            case 42:
                                program.Codes.Add(new Kerf() { Value = KerfType.Right });
                                code = GetNextCode();
                                break;

                            case 90:
                                program.Mode = Mode.Absolute;
                                code = GetNextCode();
                                break;

                            case 91:
                                program.Mode = Mode.Incremental;
                                code = GetNextCode();
                                break;

                            default:
                                code = GetNextCode();
                                break;
                        }
                        break;

                    case 'F':
                        var feedrate = new Feedrate() { Value = double.Parse(code.Value) };
                        if (code.VariableRef != null)
                            feedrate.VariableRef = code.VariableRef;
                        program.Codes.Add(feedrate);
                        code = GetNextCode();
                        break;

                    default:
                        code = GetNextCode();
                        break;
                }
            }
        }

        private void ReadLine(bool isRapid)
        {
            var line = new LinearMove();
            double x = 0;
            double y = 0;
            var layer = LayerType.Cut;
            var suppressed = false;
            string xRef = null, yRef = null;

            while (section == CodeSection.Line)
            {
                var code = GetNextCode();

                if (code == null)
                {
                    section = CodeSection.Unknown;
                    break;
                }
                switch (code.Id)
                {
                    case 'X':
                        x = double.Parse(code.Value);
                        xRef = code.VariableRef;
                        break;

                    case 'Y':
                        y = double.Parse(code.Value);
                        yRef = code.VariableRef;
                        break;

                    case ':':
                        {
                            var tags = code.Value.Trim().ToUpper().Split(':');

                            foreach (var tag in tags)
                            {
                                switch (tag)
                                {
                                    case "DISPLAY":
                                        layer = LayerType.Display;
                                        break;

                                    case "LEADIN":
                                        layer = LayerType.Leadin;
                                        break;

                                    case "LEADOUT":
                                        layer = LayerType.Leadout;
                                        break;

                                    case "SCRIBE":
                                        layer = LayerType.Scribe;
                                        break;

                                    case "SUPPRESSED":
                                        suppressed = true;
                                        break;
                                }
                            }
                            break;
                        }

                    default:
                        section = CodeSection.Unknown;
                        break;
                }
            }

            var refs = BuildVariableRefs(("X", xRef), ("Y", yRef));

            if (isRapid)
                program.Codes.Add(new RapidMove(x, y) { VariableRefs = refs });
            else
                program.Codes.Add(new LinearMove(x, y) { Layer = layer, Suppressed = suppressed, VariableRefs = refs });
        }

        private void ReadArc(RotationType rotation)
        {
            double x = 0;
            double y = 0;
            double i = 0;
            double j = 0;
            var layer = LayerType.Cut;
            var suppressed = false;
            string xRef = null, yRef = null, iRef = null, jRef = null;

            while (section == CodeSection.Arc)
            {
                var code = GetNextCode();

                if (code == null)
                {
                    section = CodeSection.Unknown;
                    break;
                }

                switch (code.Id)
                {
                    case 'X':
                        x = double.Parse(code.Value);
                        xRef = code.VariableRef;
                        break;

                    case 'Y':
                        y = double.Parse(code.Value);
                        yRef = code.VariableRef;
                        break;

                    case 'I':
                        i = double.Parse(code.Value);
                        iRef = code.VariableRef;
                        break;

                    case 'J':
                        j = double.Parse(code.Value);
                        jRef = code.VariableRef;
                        break;

                    case ':':
                        {
                            var tags = code.Value.Trim().ToUpper().Split(':');

                            foreach (var tag in tags)
                            {
                                switch (tag)
                                {
                                    case "DISPLAY":
                                        layer = LayerType.Display;
                                        break;

                                    case "LEADIN":
                                        layer = LayerType.Leadin;
                                        break;

                                    case "LEADOUT":
                                        layer = LayerType.Leadout;
                                        break;

                                    case "SCRIBE":
                                        layer = LayerType.Scribe;
                                        break;

                                    case "SUPPRESSED":
                                        suppressed = true;
                                        break;
                                }
                            }
                            break;
                        }

                    default:
                        section = CodeSection.Unknown;
                        break;
                }
            }
            program.Codes.Add(new ArcMove()
            {
                EndPoint = new Vector(x, y),
                CenterPoint = new Vector(i, j),
                Rotation = rotation,
                Layer = layer,
                Suppressed = suppressed,
                VariableRefs = BuildVariableRefs(("X", xRef), ("Y", yRef), ("I", iRef), ("J", jRef))
            });
        }

        private void ReadSubProgram()
        {
            var p = 0;
            var r = 0.0;

            while (section == CodeSection.SubProgram)
            {
                var code = GetNextCode();

                if (code == null)
                {
                    section = CodeSection.Unknown;
                    break;
                }

                switch (code.Id)
                {
                    case 'P':
                        p = int.Parse(code.Value);
                        break;

                    case 'R':
                        r = double.Parse(code.Value);
                        break;

                    default:
                        section = CodeSection.Unknown;
                        break;
                }
            }

            program.Codes.Add(new SubProgramCall() { Id = p, Rotation = r });
        }

        private Code GetNextCode()
        {
            codeIndex++;

            if (codeIndex >= block.Count)
                return null;

            return block[codeIndex];
        }

        private Code GetCurrentCode()
        {
            if (codeIndex >= block.Count)
                return null;

            return block[codeIndex];
        }

        private Code GetFirstCode()
        {
            if (block.Count == 0)
                return null;

            codeIndex = 0;
            return block[codeIndex];
        }

        private static bool TryParseVariableDefinition(string line, out string name, out string expression,
            out bool isInline, out bool isGlobal)
        {
            name = null;
            expression = null;
            isInline = false;
            isGlobal = false;

            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                return false;

            // Must start with a letter or underscore (not a G-code letter followed by a digit)
            var firstChar = trimmed[0];
            if (!char.IsLetter(firstChar) && firstChar != '_')
                return false;

            // If line starts with a known G-code letter followed by a digit, it's not a variable
            if (trimmed.Length >= 2 && char.IsDigit(trimmed[1]))
            {
                var upper = char.ToUpper(firstChar);
                if (upper is 'G' or 'M' or 'N' or 'F' or 'X' or 'Y' or 'I' or 'J' or 'T' or 'S' or 'O' or 'P' or 'R')
                    return false;
            }

            // Must contain '='
            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex < 1)
                return false;

            // Extract name (everything before '=', trimmed)
            var rawName = trimmed.Substring(0, eqIndex).Trim();

            // Validate name: must be identifier (letter/underscore followed by alphanumeric/underscore)
            if (rawName.Length == 0 || (!char.IsLetter(rawName[0]) && rawName[0] != '_'))
                return false;
            for (var i = 1; i < rawName.Length; i++)
            {
                if (!char.IsLetterOrDigit(rawName[i]) && rawName[i] != '_')
                    return false;
            }

            // Extract expression and flags from the remainder after '='
            var remainder = trimmed.Substring(eqIndex + 1).Trim();

            // Check for trailing flags: inline and/or global
            // Parse from the end to separate expression from flags
            var words = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var flagStart = words.Length;
            for (var i = words.Length - 1; i >= 0; i--)
            {
                var word = words[i].ToLowerInvariant();
                if (word == "inline" || word == "global")
                    flagStart = i;
                else
                    break;
            }

            // Build expression from non-flag words
            var expressionParts = words.Take(flagStart).ToArray();
            if (expressionParts.Length == 0)
                return false;

            expression = string.Join(" ", expressionParts);

            // Parse flags
            for (var i = flagStart; i < words.Length; i++)
            {
                var word = words[i].ToLowerInvariant();
                if (word == "inline") isInline = true;
                else if (word == "global") isGlobal = true;
            }

            name = rawName;
            return true;
        }

        private static Dictionary<string, double> ResolveVariables(
            Dictionary<string, (string expression, bool inline, bool global)> variableDefs)
        {
            if (variableDefs.Count == 0)
                return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            // Build dependency graph
            var dependencies = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in variableDefs)
            {
                var deps = new List<string>();
                var expr = kvp.Value.expression;
                for (var i = 0; i < expr.Length; i++)
                {
                    if (expr[i] == '$')
                    {
                        var start = i + 1;
                        while (start < expr.Length && (char.IsLetterOrDigit(expr[start]) || expr[start] == '_'))
                            start++;
                        var refName = expr.Substring(i + 1, start - i - 1);
                        // Find the canonical name (case-insensitive match)
                        var canonical = variableDefs.Keys
                            .FirstOrDefault(k => string.Equals(k, refName, StringComparison.OrdinalIgnoreCase));
                        if (canonical != null)
                            deps.Add(canonical);
                        i = start - 1;
                    }
                }
                dependencies[kvp.Key] = deps;
            }

            // Topological sort (Kahn's algorithm)
            var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in variableDefs.Keys)
                inDegree[name] = 0;
            foreach (var kvp in dependencies)
            {
                foreach (var dep in kvp.Value)
                {
                    if (inDegree.ContainsKey(dep))
                        inDegree[kvp.Key]++;
                }
            }

            var queue = new Queue<string>();
            foreach (var kvp in inDegree)
            {
                if (kvp.Value == 0)
                    queue.Enqueue(kvp.Key);
            }

            var resolved = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                order.Add(current);

                // Evaluate this variable
                var expr = variableDefs[current].expression;
                var value = ExpressionEvaluator.Evaluate(expr, resolved);
                resolved[current] = value;

                // Reduce in-degree of dependents
                foreach (var kvp in dependencies)
                {
                    if (kvp.Value.Contains(current, StringComparer.OrdinalIgnoreCase))
                    {
                        inDegree[kvp.Key]--;
                        if (inDegree[kvp.Key] == 0 && !order.Contains(kvp.Key))
                            queue.Enqueue(kvp.Key);
                    }
                }
            }

            if (order.Count != variableDefs.Count)
                throw new InvalidOperationException("Circular dependency detected among variables.");

            return resolved;
        }

        private static Dictionary<string, string> BuildVariableRefs(params (string axis, string varRef)[] refs)
        {
            Dictionary<string, string> result = null;
            foreach (var (axis, varRef) in refs)
            {
                if (varRef != null)
                {
                    result ??= new Dictionary<string, string>();
                    result[axis] = varRef;
                }
            }
            return result;
        }

        public void Close()
        {
            reader.Close();
        }

        private class Code
        {
            public Code(char id)
            {
                Id = id;
                Value = string.Empty;
            }

            public Code(char id, string value)
            {
                Id = id;
                Value = value;
            }

            public char Id { get; private set; }

            public string Value { get; set; }

            public string VariableRef { get; set; }

            public override string ToString()
            {
                return Id + Value;
            }
        }

        private class CodeBlock : List<Code>
        {
            public void Add(char id, string value)
            {
                Add(new Code(id, value));
            }

            public override string ToString()
            {
                var builder = new StringBuilder();
                foreach (var code in this)
                    builder.Append(code.ToString() + " ");
                return builder.ToString();
            }
        }

        private enum CodeSection
        {
            Unknown,
            Arc,
            Line,
            SubProgram
        }
    }
}
