using OpenNest.CNC;
using OpenNest.Geometry;
using System.Collections.Generic;
using System.IO;
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

        public ProgramReader(Stream stream)
        {
            reader = new StreamReader(stream);
            program = new Program();
        }

        public Program Read()
        {
            string line;

            while ((line = reader.ReadLine()) != null)
            {
                block = ParseBlock(line);
                ProcessCurrentBlock();
            }

            return program;
        }

        private CodeBlock ParseBlock(string line)
        {
            var block = new CodeBlock();
            Code code = null;
            for (int i = 0; i < line.Length; ++i)
            {
                var c = line[i];
                if (char.IsLetter(c))
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
                        program.Codes.Add(new Feedrate() { Value = double.Parse(code.Value) });
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
                        break;

                    case 'Y':
                        y = double.Parse(code.Value);
                        break;

                    case ':':
                        {
                            var value = code.Value.Trim().ToUpper();

                            switch (value)
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
                            }
                            break;
                        }

                    default:
                        section = CodeSection.Unknown;
                        break;
                }
            }
            if (isRapid)
                program.Codes.Add(new RapidMove(x, y));
            else
                program.Codes.Add(new LinearMove(x, y) { Layer = layer });
        }

        private void ReadArc(RotationType rotation)
        {
            double x = 0;
            double y = 0;
            double i = 0;
            double j = 0;
            var layer = LayerType.Cut;

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
                        break;

                    case 'Y':
                        y = double.Parse(code.Value);
                        break;

                    case 'I':
                        i = double.Parse(code.Value);
                        break;

                    case 'J':
                        j = double.Parse(code.Value);
                        break;

                    case ':':
                        {
                            var value = code.Value.Trim().ToUpper();

                            switch (value)
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
                Layer = layer
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
