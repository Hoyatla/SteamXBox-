using System.Globalization;

namespace SenSÉ.Tools.Calculator;

/// <summary>Whether the trigonometric functions read their argument in degrees or radians.</summary>
public enum AngleMode
{
    Degrees,
    Radians,
}

/// <summary>The outcome of evaluating an expression.</summary>
/// <param name="Value">The result, meaningful only when <see cref="IsSuccess"/>.</param>
/// <param name="Error">A message to show the user, or null on success.</param>
public readonly record struct EvaluationResult(double Value, string? Error)
{
    public bool IsSuccess => Error is null;

    public static EvaluationResult Ok(double value) => new(value, null);

    public static EvaluationResult Fail(string error) => new(0, error);
}

/// <summary>
/// Evaluates a scientific expression.
/// </summary>
/// <remarks>
/// A recursive-descent parser rather than an immediate-execution calculator. It means the whole
/// expression is visible and editable before it is evaluated, so precedence and parentheses behave
/// the way they do on paper: <c>2+3*4</c> is 14, not 20. That is the difference between a
/// scientific calculator and a pocket one, and it is the reason this is worth parsing properly.
///
/// Precedence, loosest first: <c>+ -</c>, then <c>* / mod</c>, then unary minus, then <c>^</c>
/// (right associative), then postfix <c>!</c>. Unary minus deliberately binds looser than the
/// power, so <c>-2^2</c> is -4, as every scientific calculator and every algebra textbook agree.
/// </remarks>
public static class ExpressionEvaluator
{
    /// <summary>Beyond this, the factorial overflows a double and the answer would be infinity.</summary>
    private const int MaxFactorial = 170;

    public static EvaluationResult Evaluate(string expression, AngleMode angleMode = AngleMode.Degrees)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return EvaluationResult.Fail("Expression vide");
        }

        if (!Tokenizer.TryTokenize(expression, out var tokens, out var tokenError))
        {
            return EvaluationResult.Fail(tokenError!);
        }

        if (tokens.Count == 0)
        {
            return EvaluationResult.Fail("Expression vide");
        }

        var parser = new Parser(tokens, angleMode);
        try
        {
            var value = parser.ParseExpression();
            if (!parser.AtEnd)
            {
                return EvaluationResult.Fail("Expression incomplete");
            }

            if (double.IsNaN(value))
            {
                return EvaluationResult.Fail("Resultat indefini");
            }

            if (double.IsInfinity(value))
            {
                return EvaluationResult.Fail("Depassement de capacite");
            }

            return EvaluationResult.Ok(value);
        }
        catch (CalculatorException exception)
        {
            return EvaluationResult.Fail(exception.Message);
        }
    }

    /// <summary>
    /// Formats a result for the display.
    /// </summary>
    /// <remarks>
    /// Fifteen significant digits is the most a double can carry honestly; showing seventeen would
    /// expose the binary representation's noise, and <c>0.1 + 0.2</c> would read as
    /// 0.30000000000000004. Round-tripping through <c>G15</c> hides that without lying about
    /// anything a user could act on.
    /// </remarks>
    public static string Format(double value)
    {
        if (value == 0)
        {
            // Otherwise a negative zero, which arises from things like -1 * 0, prints as "-0".
            return "0";
        }

        var magnitude = Math.Abs(value);
        var text = magnitude is >= 1e15 or < 1e-9
            ? value.ToString("G15", CultureInfo.InvariantCulture)
            : value.ToString("0.###############", CultureInfo.InvariantCulture);

        return text;
    }

    private sealed class CalculatorException(string message) : Exception(message);

    private sealed class Parser(List<Token> tokens, AngleMode angleMode)
    {
        private int _index;

        public bool AtEnd => _index >= tokens.Count;

        private Token Current => tokens[_index];

        public double ParseExpression()
        {
            var value = ParseTerm();
            while (!AtEnd && Current.Kind == TokenKind.Operator && Current.Text is "+" or "-")
            {
                var op = Current.Text;
                _index++;
                var right = ParseTerm();
                value = op == "+" ? value + right : value - right;
            }

            return value;
        }

        private double ParseTerm()
        {
            var value = ParseUnary();
            while (!AtEnd)
            {
                if (Current.Kind == TokenKind.Operator && Current.Text is "*" or "/" or "mod")
                {
                    var op = Current.Text;
                    _index++;
                    var right = ParseUnary();
                    value = op switch
                    {
                        "*" => value * right,
                        "/" => right == 0 ? throw new CalculatorException("Division par zero") : value / right,
                        _ => right == 0 ? throw new CalculatorException("Division par zero") : value % right,
                    };
                    continue;
                }

                // Implicit multiplication: 2pi, 3(4+5), 2sin(30). Deliberately not between two
                // numbers — "12 34" is a typo, not a product, and silently reading it as 408 would
                // hide the mistake.
                if (Current.Kind is TokenKind.Constant or TokenKind.Function or TokenKind.LeftParen)
                {
                    value *= ParseUnary();
                    continue;
                }

                break;
            }

            return value;
        }

        private double ParseUnary()
        {
            if (!AtEnd && Current.Kind == TokenKind.Operator && Current.Text is "+" or "-")
            {
                var negate = Current.Text == "-";
                _index++;
                var value = ParseUnary();
                return negate ? -value : value;
            }

            return ParsePower();
        }

        private double ParsePower()
        {
            var value = ParsePostfix();
            if (!AtEnd && Current.Kind == TokenKind.Operator && Current.Text == "^")
            {
                _index++;

                // Right associative, and the exponent may itself be signed: 2^-3, 2^2^3.
                var exponent = ParseUnary();
                return Math.Pow(value, exponent);
            }

            return value;
        }

        private double ParsePostfix()
        {
            var value = ParsePrimary();
            while (!AtEnd && Current.Kind == TokenKind.Factorial)
            {
                _index++;
                value = Factorial(value);
            }

            return value;
        }

        private double ParsePrimary()
        {
            if (AtEnd)
            {
                throw new CalculatorException("Expression incomplete");
            }

            var token = Current;
            switch (token.Kind)
            {
                case TokenKind.Number or TokenKind.Constant:
                    _index++;
                    return token.Value;

                case TokenKind.LeftParen:
                {
                    _index++;
                    var inner = ParseExpression();
                    Expect(TokenKind.RightParen, "Parenthese fermante manquante");
                    return inner;
                }

                case TokenKind.Function:
                {
                    _index++;
                    Expect(TokenKind.LeftParen, $"Parenthese attendue apres {token.Text}");
                    var argument = ParseExpression();
                    Expect(TokenKind.RightParen, "Parenthese fermante manquante");
                    return ApplyFunction(token.Text, argument);
                }

                default:
                    throw new CalculatorException("Expression invalide");
            }
        }

        private void Expect(TokenKind kind, string message)
        {
            if (AtEnd || Current.Kind != kind)
            {
                throw new CalculatorException(message);
            }

            _index++;
        }

        private double ApplyFunction(string name, double argument) => name switch
        {
            "sin" => Math.Sin(ToRadians(argument)),
            "cos" => Math.Cos(ToRadians(argument)),
            "tan" => Math.Tan(ToRadians(argument)),
            "asin" => FromRadians(Math.Asin(Domain(argument, -1, 1))),
            "acos" => FromRadians(Math.Acos(Domain(argument, -1, 1))),
            "atan" => FromRadians(Math.Atan(argument)),
            "sinh" => Math.Sinh(argument),
            "cosh" => Math.Cosh(argument),
            "tanh" => Math.Tanh(argument),
            "asinh" => Math.Asinh(argument),
            "acosh" => argument < 1 ? throw new CalculatorException("Domaine invalide") : Math.Acosh(argument),
            "atanh" => Math.Abs(argument) >= 1 ? throw new CalculatorException("Domaine invalide") : Math.Atanh(argument),
            "ln" => argument <= 0 ? throw new CalculatorException("Domaine invalide") : Math.Log(argument),
            "log" => argument <= 0 ? throw new CalculatorException("Domaine invalide") : Math.Log10(argument),
            "sqrt" => argument < 0 ? throw new CalculatorException("Racine d'un nombre negatif") : Math.Sqrt(argument),
            "cbrt" => Math.Cbrt(argument),
            "abs" => Math.Abs(argument),
            "exp" => Math.Exp(argument),
            _ => throw new CalculatorException($"Fonction inconnue : {name}"),
        };

        private static double Domain(double value, double low, double high) =>
            value < low || value > high ? throw new CalculatorException("Domaine invalide") : value;

        private double ToRadians(double value) =>
            angleMode == AngleMode.Degrees ? value * Math.PI / 180.0 : value;

        private double FromRadians(double value) =>
            angleMode == AngleMode.Degrees ? value * 180.0 / Math.PI : value;

        private static double Factorial(double value)
        {
            if (value < 0 || value != Math.Floor(value))
            {
                throw new CalculatorException("Factorielle : entier positif attendu");
            }

            if (value > MaxFactorial)
            {
                throw new CalculatorException("Depassement de capacite");
            }

            var result = 1.0;
            for (var i = 2; i <= (int)value; i++)
            {
                result *= i;
            }

            return result;
        }
    }
}
