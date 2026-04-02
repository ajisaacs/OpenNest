using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenNest.Math
{
    /// <summary>
    /// Recursive descent parser for simple arithmetic expressions supporting
    /// +, -, *, /, parentheses, unary minus/plus, and $variable references.
    /// </summary>
    public static class ExpressionEvaluator
    {
        public static double Evaluate(string expression, IReadOnlyDictionary<string, double> variables)
        {
            var parser = new Parser(expression, variables);
            var result = parser.ParseExpression();
            parser.SkipWhitespace();
            if (!parser.IsEnd)
                throw new FormatException($"Unexpected character at position {parser.Position}: '{parser.Current}'");
            return result;
        }

        private ref struct Parser
        {
            private readonly ReadOnlySpan<char> _input;
            private readonly IReadOnlyDictionary<string, double> _variables;
            private int _pos;

            public Parser(string input, IReadOnlyDictionary<string, double> variables)
            {
                _input = input.AsSpan();
                _variables = variables;
                _pos = 0;
            }

            public int Position => _pos;
            public bool IsEnd => _pos >= _input.Length;
            public char Current => _input[_pos];

            public void SkipWhitespace()
            {
                while (_pos < _input.Length && _input[_pos] == ' ')
                    _pos++;
            }

            // Expression = Term (('+' | '-') Term)*
            public double ParseExpression()
            {
                SkipWhitespace();
                var left = ParseTerm();

                while (true)
                {
                    SkipWhitespace();
                    if (IsEnd) break;

                    var op = Current;
                    if (op != '+' && op != '-') break;

                    _pos++;
                    SkipWhitespace();
                    var right = ParseTerm();

                    left = op == '+' ? left + right : left - right;
                }

                return left;
            }

            // Term = Unary (('*' | '/') Unary)*
            private double ParseTerm()
            {
                var left = ParseUnary();

                while (true)
                {
                    SkipWhitespace();
                    if (IsEnd) break;

                    var op = Current;
                    if (op != '*' && op != '/') break;

                    _pos++;
                    SkipWhitespace();
                    var right = ParseUnary();

                    left = op == '*' ? left * right : left / right;
                }

                return left;
            }

            // Unary = ('-' | '+')? Primary
            private double ParseUnary()
            {
                SkipWhitespace();
                if (!IsEnd && Current == '-')
                {
                    _pos++;
                    return -ParsePrimary();
                }
                if (!IsEnd && Current == '+')
                {
                    _pos++;
                }
                return ParsePrimary();
            }

            // Primary = '(' Expression ')' | '$' Identifier | Number
            private double ParsePrimary()
            {
                SkipWhitespace();

                if (IsEnd)
                    throw new FormatException("Unexpected end of expression.");

                if (Current == '(')
                {
                    _pos++; // consume '('
                    var value = ParseExpression();
                    SkipWhitespace();
                    if (IsEnd || Current != ')')
                        throw new FormatException("Expected closing parenthesis.");
                    _pos++; // consume ')'
                    return value;
                }

                if (Current == '$')
                {
                    _pos++; // consume '$'
                    var start = _pos;
                    while (_pos < _input.Length && (char.IsLetterOrDigit(_input[_pos]) || _input[_pos] == '_'))
                        _pos++;
                    if (_pos == start)
                        throw new FormatException("Expected variable name after '$'.");
                    var name = _input.Slice(start, _pos - start).ToString();
                    if (!_variables.TryGetValue(name, out var varValue))
                        throw new KeyNotFoundException($"Undefined variable: ${name}");
                    return varValue;
                }

                // Number
                var numStart = _pos;
                while (_pos < _input.Length && (char.IsDigit(_input[_pos]) || _input[_pos] == '.'))
                    _pos++;

                if (_pos == numStart)
                    throw new FormatException($"Unexpected character '{Current}' at position {_pos}.");

                var numSpan = _input.Slice(numStart, _pos - numStart).ToString();
                if (!double.TryParse(numSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    throw new FormatException($"Invalid number: '{numSpan}'");

                return number;
            }
        }
    }
}
