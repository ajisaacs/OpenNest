using System.Collections.Generic;
using OpenNest.Math;

namespace OpenNest.Tests.Math;

public class ExpressionEvaluatorTests
{
    private static readonly Dictionary<string, double> Empty = new();

    [Fact]
    public void Evaluate_NumericLiteral()
    {
        Assert.Equal(42.0, ExpressionEvaluator.Evaluate("42", Empty));
    }

    [Fact]
    public void Evaluate_DecimalLiteral()
    {
        Assert.Equal(0.3, ExpressionEvaluator.Evaluate("0.3", Empty));
    }

    [Fact]
    public void Evaluate_NegativeLiteral()
    {
        Assert.Equal(-5.0, ExpressionEvaluator.Evaluate("-5", Empty));
    }

    [Fact]
    public void Evaluate_Addition()
    {
        Assert.Equal(5.0, ExpressionEvaluator.Evaluate("2 + 3", Empty));
    }

    [Fact]
    public void Evaluate_Subtraction()
    {
        Assert.Equal(1.0, ExpressionEvaluator.Evaluate("3 - 2", Empty));
    }

    [Fact]
    public void Evaluate_Multiplication()
    {
        Assert.Equal(6.0, ExpressionEvaluator.Evaluate("2 * 3", Empty));
    }

    [Fact]
    public void Evaluate_Division()
    {
        Assert.Equal(2.5, ExpressionEvaluator.Evaluate("5 / 2", Empty));
    }

    [Fact]
    public void Evaluate_OperatorPrecedence()
    {
        Assert.Equal(7.0, ExpressionEvaluator.Evaluate("1 + 2 * 3", Empty));
    }

    [Fact]
    public void Evaluate_Parentheses()
    {
        Assert.Equal(9.0, ExpressionEvaluator.Evaluate("(1 + 2) * 3", Empty));
    }

    [Fact]
    public void Evaluate_VariableReference()
    {
        var vars = new Dictionary<string, double> { { "diameter", 0.3 } };
        Assert.Equal(0.3, ExpressionEvaluator.Evaluate("$diameter", vars));
    }

    [Fact]
    public void Evaluate_VariableInExpression()
    {
        var vars = new Dictionary<string, double> { { "diameter", 0.6 } };
        Assert.Equal(0.3, ExpressionEvaluator.Evaluate("$diameter / 2", vars));
    }

    [Fact]
    public void Evaluate_MultipleVariables()
    {
        var vars = new Dictionary<string, double> { { "x", 2.0 }, { "y", 3.0 } };
        Assert.Equal(5.0, ExpressionEvaluator.Evaluate("$x + $y", vars));
    }

    [Fact]
    public void Evaluate_CaseInsensitiveVariables()
    {
        var vars = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "Diameter", 0.3 }
        };
        Assert.Equal(0.3, ExpressionEvaluator.Evaluate("$diameter", vars));
    }

    [Fact]
    public void Evaluate_UndefinedVariable_Throws()
    {
        Assert.Throws<KeyNotFoundException>(() =>
            ExpressionEvaluator.Evaluate("$missing", Empty));
    }

    [Fact]
    public void Evaluate_NestedParentheses()
    {
        Assert.Equal(20.0, ExpressionEvaluator.Evaluate("((2 + 3) * (1 + 1)) * 2", Empty));
    }

    [Fact]
    public void Evaluate_UnaryMinusBeforeParens()
    {
        Assert.Equal(-6.0, ExpressionEvaluator.Evaluate("-(2 + 4)", Empty));
    }

    [Fact]
    public void Evaluate_WhitespaceHandling()
    {
        Assert.Equal(5.0, ExpressionEvaluator.Evaluate("  2  +  3  ", Empty));
    }
}
