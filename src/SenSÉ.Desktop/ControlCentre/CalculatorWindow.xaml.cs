using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SenSÉ.Tools.Calculator;

namespace SenSÉ.Desktop.ControlCentre;

/// <summary>What a key does when pressed.</summary>
public enum CalcKeyKind
{
    /// <summary>Appends <see cref="CalcKey.Insert"/> to the expression.</summary>
    Insert,
    Equals,
    Clear,
    Backspace,
    ToggleAngle,
    Negate,
    Answer,
    MemoryClear,
    MemoryRecall,
    MemoryStore,
    MemoryAdd,
    MemorySubtract,
}

/// <summary>One key of the keypad.</summary>
/// <param name="Label">What is written on the key.</param>
/// <param name="Kind">What pressing it does.</param>
/// <param name="Insert">Text appended for <see cref="CalcKeyKind.Insert"/> keys; the label when empty.</param>
/// <param name="Accent">Whether the key is drawn in the accent colour — operators and actions.</param>
public sealed record CalcKey(string Label, CalcKeyKind Kind = CalcKeyKind.Insert, string Insert = "", bool Accent = false)
{
    public string Text => Insert.Length > 0 ? Insert : Label;
}

/// <summary>
/// A scientific calculator.
/// </summary>
/// <remarks>
/// The expression is built up and shown in full, then evaluated on <c>=</c>. That is what makes it
/// scientific rather than a pocket calculator: <c>2+3*4</c> is 14, parentheses work, and a mistake
/// three keys ago can be seen and backspaced instead of forcing a restart.
///
/// All the arithmetic lives in <see cref="ExpressionEvaluator"/>, which is a separate library with
/// its own tests. This file only turns key presses into text and shows the answer.
/// </remarks>
public partial class CalculatorWindow : Window
{
    private AngleMode _angleMode = AngleMode.Degrees;
    private double _memory;
    private double _lastAnswer;
    private bool _showingResult;

    public CalculatorWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            KeyPad.ApplyTemplate();
            KeyPad.UpdateLayout();
            MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        };
    }

    #region Bound state

    public static readonly DependencyProperty ExpressionProperty =
        DependencyProperty.Register(nameof(Expression), typeof(string), typeof(CalculatorWindow),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DisplayProperty =
        DependencyProperty.Register(nameof(Display), typeof(string), typeof(CalculatorWindow),
            new PropertyMetadata("0"));

    public static readonly DependencyProperty DisplayBrushProperty =
        DependencyProperty.Register(nameof(DisplayBrush), typeof(Brush), typeof(CalculatorWindow),
            new PropertyMetadata(default(Brush)));

    public static readonly DependencyProperty AngleLabelProperty =
        DependencyProperty.Register(nameof(AngleLabel), typeof(string), typeof(CalculatorWindow),
            new PropertyMetadata("DEG"));

    public static readonly DependencyProperty MemoryIndicatorProperty =
        DependencyProperty.Register(nameof(MemoryIndicator), typeof(Visibility), typeof(CalculatorWindow),
            new PropertyMetadata(Visibility.Collapsed));

    /// <summary>The expression being built, shown small above the result.</summary>
    public string Expression
    {
        get => (string)GetValue(ExpressionProperty);
        set => SetValue(ExpressionProperty, value);
    }

    /// <summary>The result, or the error message.</summary>
    public string Display
    {
        get => (string)GetValue(DisplayProperty);
        set => SetValue(DisplayProperty, value);
    }

    public Brush DisplayBrush
    {
        get => (Brush)GetValue(DisplayBrushProperty);
        set => SetValue(DisplayBrushProperty, value);
    }

    public string AngleLabel
    {
        get => (string)GetValue(AngleLabelProperty);
        set => SetValue(AngleLabelProperty, value);
    }

    public Visibility MemoryIndicator
    {
        get => (Visibility)GetValue(MemoryIndicatorProperty);
        set => SetValue(MemoryIndicatorProperty, value);
    }

    #endregion

    /// <summary>
    /// The keypad, six to a row.
    /// </summary>
    /// <remarks>
    /// Data rather than fifty buttons in the XAML: the layout is then one readable table, and
    /// moving a key is moving a line.
    /// </remarks>
    public IReadOnlyList<CalcKey> Keys { get; } =
    [
        new("DEG", CalcKeyKind.ToggleAngle, Accent: true),
        new("MC", CalcKeyKind.MemoryClear), new("MR", CalcKeyKind.MemoryRecall),
        new("MS", CalcKeyKind.MemoryStore), new("M+", CalcKeyKind.MemoryAdd),
        new("M-", CalcKeyKind.MemorySubtract),

        new("sin", Insert: "sin("), new("cos", Insert: "cos("), new("tan", Insert: "tan("),
        new("ln", Insert: "ln("), new("log", Insert: "log("), new("n!", Insert: "!"),

        new("asin", Insert: "asin("), new("acos", Insert: "acos("), new("atan", Insert: "atan("),
        new("eˣ", Insert: "exp("), new("π", Insert: "π"), new("e", Insert: "e"),

        new("("), new(")"), new("√", Insert: "√("), new("∛", Insert: "cbrt("),
        new("xʸ", Insert: "^", Accent: true), new("mod", Insert: " mod ", Accent: true),

        new("7"), new("8"), new("9"),
        new("÷", Insert: "÷", Accent: true),
        new("C", CalcKeyKind.Clear, Accent: true),
        new("⌫", CalcKeyKind.Backspace, Accent: true),

        new("4"), new("5"), new("6"),
        new("×", Insert: "×", Accent: true),
        new("|x|", Insert: "abs("), new("x²", Insert: "^2"),

        new("1"), new("2"), new("3"),
        new("−", Insert: "-", Accent: true),
        new("±", CalcKeyKind.Negate), new("1/x", Insert: "1/("),

        new("0"), new("."), new("E", Insert: "e"),
        new("+", Insert: "+", Accent: true),
        new("Ans", CalcKeyKind.Answer),
        new("=", CalcKeyKind.Equals, Accent: true),
    ];

    private void Key_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CalcKey key })
        {
            Press(key);
        }
    }

    private void Press(CalcKey key)
    {
        switch (key.Kind)
        {
            case CalcKeyKind.Insert:
                Append(key.Text);
                break;

            case CalcKeyKind.Equals:
                Evaluate();
                break;

            case CalcKeyKind.Clear:
                Expression = string.Empty;
                SetResult("0", isError: false);
                _showingResult = false;
                break;

            case CalcKeyKind.Backspace:
                if (Expression.Length > 0)
                {
                    Expression = Expression[..^1];
                }

                _showingResult = false;
                break;

            case CalcKeyKind.ToggleAngle:
                _angleMode = _angleMode == AngleMode.Degrees ? AngleMode.Radians : AngleMode.Degrees;
                AngleLabel = _angleMode == AngleMode.Degrees ? "DEG" : "RAD";

                // The answer on screen was computed in the other mode; recompute rather than leave
                // a number that no longer matches what the expression now means.
                if (Expression.Length > 0)
                {
                    Evaluate();
                }

                break;

            case CalcKeyKind.Negate:
                // Wraps the whole expression rather than trying to find "the current number":
                // with a freely edited expression there is no such thing, and guessing would be
                // wrong exactly when the expression is complicated enough to matter.
                if (Expression.Length > 0)
                {
                    Expression = Expression.StartsWith("-(") && Expression.EndsWith(')')
                        ? Expression[2..^1]
                        : $"-({Expression})";
                }

                break;

            case CalcKeyKind.Answer:
                Append(ExpressionEvaluator.Format(_lastAnswer));
                break;

            case CalcKeyKind.MemoryClear:
                _memory = 0;
                MemoryIndicator = Visibility.Collapsed;
                break;

            case CalcKeyKind.MemoryRecall:
                Append(ExpressionEvaluator.Format(_memory));
                break;

            case CalcKeyKind.MemoryStore:
                if (TryCurrentValue(out var stored))
                {
                    _memory = stored;
                    MemoryIndicator = Visibility.Visible;
                }

                break;

            case CalcKeyKind.MemoryAdd:
                if (TryCurrentValue(out var added))
                {
                    _memory += added;
                    MemoryIndicator = Visibility.Visible;
                }

                break;

            case CalcKeyKind.MemorySubtract:
                if (TryCurrentValue(out var subtracted))
                {
                    _memory -= subtracted;
                    MemoryIndicator = Visibility.Visible;
                }

                break;
        }
    }

    /// <summary>
    /// Appends to the expression, starting a new one if the last thing shown was a result.
    /// </summary>
    /// <remarks>
    /// Typing a digit straight after <c>=</c> starts a fresh calculation, but typing an operator
    /// continues from the answer — which is what every calculator does and what the hand expects.
    /// </remarks>
    private void Append(string text)
    {
        if (_showingResult)
        {
            var continues = text.Length > 0 && (text[0] is '+' or '-' or '×' or '÷' or '^' or ' ' || text.StartsWith(" mod"));
            Expression = continues ? ExpressionEvaluator.Format(_lastAnswer) : string.Empty;
            _showingResult = false;
        }

        Expression += text;
    }

    private void Evaluate()
    {
        var result = ExpressionEvaluator.Evaluate(Expression, _angleMode);
        if (result.IsSuccess)
        {
            _lastAnswer = result.Value;
            SetResult(ExpressionEvaluator.Format(result.Value), isError: false);
            _showingResult = true;
        }
        else
        {
            SetResult(result.Error!, isError: true);
            _showingResult = false;
        }
    }

    /// <summary>The value of the expression as it stands, for the memory keys.</summary>
    private bool TryCurrentValue(out double value)
    {
        if (Expression.Length == 0)
        {
            value = _lastAnswer;
            return true;
        }

        var result = ExpressionEvaluator.Evaluate(Expression, _angleMode);
        value = result.Value;
        return result.IsSuccess;
    }

    private void SetResult(string text, bool isError)
    {
        Display = text;
        DisplayBrush = (Brush)FindResource(isError ? "AccentRedBrush" : "TextPrimaryBrush");
    }

    /// <summary>
    /// Keyboard input.
    /// </summary>
    /// <remarks>
    /// Handled at the window rather than in a text box so that the arrow keys stay free to walk the
    /// keypad — that is what makes the same window usable with a gamepad. Everything a calculator
    /// keyboard offers is here; the scientific functions have no obvious key and stay on the pad.
    /// </remarks>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var handled = true;
        switch (e.Key)
        {
            case >= Key.D0 and <= Key.D9 when e.KeyboardDevice.Modifiers == ModifierKeys.None:
                Append(((char)('0' + (e.Key - Key.D0))).ToString());
                break;
            case >= Key.NumPad0 and <= Key.NumPad9:
                Append(((char)('0' + (e.Key - Key.NumPad0))).ToString());
                break;
            case Key.Add or Key.OemPlus when e.KeyboardDevice.Modifiers == ModifierKeys.None:
                Append("+");
                break;
            case Key.Subtract or Key.OemMinus:
                Append("-");
                break;
            case Key.Multiply:
                Append("×");
                break;
            case Key.Divide:
                Append("÷");
                break;
            case Key.Decimal or Key.OemPeriod or Key.OemComma:
                Append(".");
                break;
            case Key.Enter or Key.Return:
                Evaluate();
                break;
            case Key.Back:
                Press(new CalcKey("⌫", CalcKeyKind.Backspace));
                break;
            case Key.Escape:
                Press(new CalcKey("C", CalcKeyKind.Clear));
                break;
            default:
                handled = false;
                break;
        }

        if (handled)
        {
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
