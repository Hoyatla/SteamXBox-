using SteamXBox.Tools.Calculator;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

public class ExpressionEvaluatorTests
{
    private static double Eval(string expression, AngleMode mode = AngleMode.Degrees)
    {
        var result = ExpressionEvaluator.Evaluate(expression, mode);
        Assert.True(result.IsSuccess, $"'{expression}' a echoue : {result.Error}");
        return result.Value;
    }

    private static string Error(string expression)
    {
        var result = ExpressionEvaluator.Evaluate(expression);
        Assert.False(result.IsSuccess, $"'{expression}' aurait du echouer, a rendu {result.Value}");
        return result.Error!;
    }

    [Theory]
    [InlineData("1+2", 3)]
    [InlineData("7-9", -2)]
    [InlineData("6*7", 42)]
    [InlineData("9/2", 4.5)]
    [InlineData("10 mod 3", 1)]
    [InlineData("10%3", 1)]
    public void EvaluatesTheBasicOperators(string expression, double expected)
        => Assert.Equal(expected, Eval(expression), 10);

    // The whole reason for parsing rather than executing key by key: a pocket calculator gives 20.
    [Fact]
    public void MultiplicationBindsTighterThanAddition()
        => Assert.Equal(14, Eval("2+3*4"), 10);

    [Fact]
    public void ParenthesesOverridePrecedence()
        => Assert.Equal(20, Eval("(2+3)*4"), 10);

    [Fact]
    public void PowerBindsTighterThanUnaryMinus()
        => Assert.Equal(-4, Eval("-2^2"), 10);

    [Fact]
    public void PowerIsRightAssociative()
        => Assert.Equal(512, Eval("2^3^2"), 10);

    [Fact]
    public void PowerAcceptsASignedExponent()
        => Assert.Equal(0.125, Eval("2^-3"), 10);

    [Theory]
    [InlineData("2pi", 6.283185307)]
    [InlineData("3(4+5)", 27)]
    [InlineData("2sqrt(9)", 6)]
    public void MultipliesImplicitly(string expression, double expected)
        => Assert.Equal(expected, Eval(expression), 8);

    // "12 34" is a typo. Reading it as a product would silently turn it into 408.
    [Fact]
    public void DoesNotMultiplyTwoAdjacentNumbers()
        => Assert.Contains("incomplete", Error("12 34"));

    [Theory]
    [InlineData("sin(30)", 0.5)]
    [InlineData("cos(60)", 0.5)]
    [InlineData("asin(0.5)", 30)]
    public void TrigonometryUsesDegreesByDefault(string expression, double expected)
        => Assert.Equal(expected, Eval(expression), 8);

    [Fact]
    public void TrigonometryHonoursRadians()
        => Assert.Equal(1, Eval("sin(pi/2)", AngleMode.Radians), 10);

    [Theory]
    [InlineData("ln(e)", 1)]
    [InlineData("log(1000)", 3)]
    [InlineData("sqrt(16)", 4)]
    [InlineData("cbrt(27)", 3)]
    [InlineData("abs(-5)", 5)]
    [InlineData("5!", 120)]
    [InlineData("0!", 1)]
    public void EvaluatesTheScientificFunctions(string expression, double expected)
        => Assert.Equal(expected, Eval(expression), 10);

    // 'e' is Euler's number, except directly after a number and before digits, where it is an
    // exponent. Both spellings have to keep working.
    [Theory]
    [InlineData("1.5e3", 1500)]
    [InlineData("2e-2", 0.02)]
    [InlineData("2e", 5.436563657)]
    public void ReadsTheExponentMarkerAndTheConstantApart(string expression, double expected)
        => Assert.Equal(expected, Eval(expression), 8);

    [Theory]
    [InlineData("1/0", "Division par zero")]
    [InlineData("10 mod 0", "Division par zero")]
    [InlineData("sqrt(-1)", "Racine d'un nombre negatif")]
    [InlineData("ln(0)", "Domaine invalide")]
    [InlineData("asin(2)", "Domaine invalide")]
    [InlineData("(-1)!", "Factorielle : entier positif attendu")]
    [InlineData("2.5!", "Factorielle : entier positif attendu")]
    [InlineData("171!", "Depassement de capacite")]
    public void ReportsMathematicalErrorsRatherThanReturningNaN(string expression, string expected)
        => Assert.Equal(expected, Error(expression));

    [Theory]
    [InlineData("(1+2")]
    [InlineData("1+")]
    [InlineData("*3")]
    [InlineData("sin 30")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1+@")]
    public void RejectsMalformedInput(string expression)
        => Assert.False(ExpressionEvaluator.Evaluate(expression).IsSuccess);

    [Fact]
    public void AcceptsTheTypographicOperatorsTheKeypadEmits()
        => Assert.Equal(6, Eval("2×3"), 10);

    [Fact]
    public void AcceptsTheRootAndPiSymbols()
        => Assert.Equal(3, Eval("√(9)"), 10);

    // G17 would expose the binary representation and print 0.30000000000000004.
    [Fact]
    public void FormattingHidesBinaryRepresentationNoise()
        => Assert.Equal("0.3", ExpressionEvaluator.Format(Eval("0.1+0.2")));

    [Fact]
    public void FormattingNeverPrintsNegativeZero()
        => Assert.Equal("0", ExpressionEvaluator.Format(Eval("-1*0")));

    [Theory]
    [InlineData(42, "42")]
    [InlineData(-3.5, "-3.5")]
    [InlineData(1e20, "1E+20")]
    public void FormatsResultsReadably(double value, string expected)
        => Assert.Equal(expected, ExpressionEvaluator.Format(value));
}
