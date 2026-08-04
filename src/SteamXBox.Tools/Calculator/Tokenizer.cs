using System.Globalization;

namespace SteamXBox.Tools.Calculator;

internal enum TokenKind
{
    Number,
    Constant,
    Function,
    Operator,
    LeftParen,
    RightParen,
    Factorial,
}

internal readonly record struct Token(TokenKind Kind, string Text, double Value = 0);

/// <summary>Turns an expression string into tokens.</summary>
/// <remarks>
/// Written by hand rather than with a regular expression because of one genuine ambiguity: the
/// letter <c>e</c> is both Euler's number and the exponent marker in <c>1.5e3</c>. It is only an
/// exponent when it sits directly after a number and is followed by digits, optionally signed —
/// everything else is the constant. A regular expression can express that, but not readably.
/// </remarks>
internal static class Tokenizer
{
    /// <summary>Function names accepted, lower case.</summary>
    internal static readonly string[] Functions =
    [
        // Longest first: the scanner takes the first match, so "asin" must be tried before "a"
        // would ever be, and "acos" before "cos" could match the tail of it.
        "asinh", "acosh", "atanh",
        "asin", "acos", "atan",
        "sinh", "cosh", "tanh",
        "sin", "cos", "tan",
        "sqrt", "cbrt", "abs", "exp", "ln", "log",
    ];

    public static bool TryTokenize(string expression, out List<Token> tokens, out string? error)
    {
        tokens = [];
        error = null;

        var index = 0;
        while (index < expression.Length)
        {
            var c = expression[index];

            if (char.IsWhiteSpace(c))
            {
                index++;
                continue;
            }

            if (char.IsAsciiDigit(c) || c == '.')
            {
                if (!TryReadNumber(expression, ref index, out var value))
                {
                    error = "Nombre invalide";
                    return false;
                }

                tokens.Add(new Token(TokenKind.Number, "", value));
                continue;
            }

            if (TryReadWord(expression, ref index, tokens, out var wordError))
            {
                continue;
            }

            if (wordError is not null)
            {
                error = wordError;
                return false;
            }

            switch (c)
            {
                case '+' or '-' or '*' or '/' or '^' or '%':
                    // '%' is accepted as a spelling of mod: it is what a keyboard offers, and this
                    // calculator has no percent operator for it to be confused with.
                    tokens.Add(new Token(TokenKind.Operator, c == '%' ? "mod" : c.ToString()));
                    index++;
                    continue;
                case '(':
                    tokens.Add(new Token(TokenKind.LeftParen, "("));
                    index++;
                    continue;
                case ')':
                    tokens.Add(new Token(TokenKind.RightParen, ")"));
                    index++;
                    continue;
                case '!':
                    tokens.Add(new Token(TokenKind.Factorial, "!"));
                    index++;
                    continue;
                case '×':  // ×
                    tokens.Add(new Token(TokenKind.Operator, "*"));
                    index++;
                    continue;
                case '÷':  // ÷
                    tokens.Add(new Token(TokenKind.Operator, "/"));
                    index++;
                    continue;
                case '−':  // − true minus sign
                    tokens.Add(new Token(TokenKind.Operator, "-"));
                    index++;
                    continue;
                case 'π':  // π
                    tokens.Add(new Token(TokenKind.Constant, "pi", Math.PI));
                    index++;
                    continue;
                case '√':  // √
                    tokens.Add(new Token(TokenKind.Function, "sqrt"));
                    index++;
                    continue;
                default:
                    error = $"Caractere inattendu : {c}";
                    return false;
            }
        }

        return true;
    }

    private static bool TryReadNumber(string text, ref int index, out double value)
    {
        var start = index;
        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            index++;
        }

        if (index < text.Length && text[index] == '.')
        {
            index++;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
            {
                index++;
            }
        }

        // Exponent, but only when it is actually one: "2e3" is a number, "2e" is 2 times e.
        if (index < text.Length && (text[index] is 'e' or 'E'))
        {
            var after = index + 1;
            if (after < text.Length && (text[after] is '+' or '-'))
            {
                after++;
            }

            if (after < text.Length && char.IsAsciiDigit(text[after]))
            {
                index = after;
                while (index < text.Length && char.IsAsciiDigit(text[index]))
                {
                    index++;
                }
            }
        }

        return double.TryParse(text[start..index], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Reads a function name, a constant or the <c>mod</c> operator.
    /// </summary>
    /// <returns>True when a word was consumed; false with <paramref name="error"/> null when the
    /// character does not start one.</returns>
    private static bool TryReadWord(string text, ref int index, List<Token> tokens, out string? error)
    {
        error = null;
        if (!char.IsAsciiLetter(text[index]))
        {
            return false;
        }

        var remaining = text.AsSpan(index);

        if (remaining.StartsWith("mod", StringComparison.OrdinalIgnoreCase))
        {
            tokens.Add(new Token(TokenKind.Operator, "mod"));
            index += 3;
            return true;
        }

        foreach (var function in Functions)
        {
            if (remaining.StartsWith(function, StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(new Token(TokenKind.Function, function));
                index += function.Length;
                return true;
            }
        }

        if (remaining.StartsWith("pi", StringComparison.OrdinalIgnoreCase))
        {
            tokens.Add(new Token(TokenKind.Constant, "pi", Math.PI));
            index += 2;
            return true;
        }

        if (text[index] is 'e' or 'E')
        {
            tokens.Add(new Token(TokenKind.Constant, "e", Math.E));
            index++;
            return true;
        }

        error = $"Nom inconnu : {text[index..]}";
        return false;
    }
}
