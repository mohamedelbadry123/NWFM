using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FormEngine.Application.Common.Schema.Expressions;

/// <summary>
/// A computed column's value expression: a small arithmetic language over a fill's answers, parsed
/// once and evaluated per fill. Nothing in it can reach beyond the answers — there is no reflection,
/// no dynamic code and no loop — and its size is bounded, so an author cannot write one that hangs
/// or exhausts the server.
/// <code>
/// expr    := term (('+' | '-') term)*
/// term    := unary (('*' | '/') unary)*
/// unary   := ('-' | '+') unary | primary
/// primary := number | 'text' | field | function '(' [expr (',' expr)*] ')' | '(' expr ')'
/// </code>
/// A <c>field</c> is a data name; a name followed by <c>(</c> is a function — <c>sum</c>, <c>min</c>,
/// <c>max</c>, <c>round</c>, <c>abs</c>, <c>coalesce</c>, <c>concat</c>. Arithmetic is decimal; a
/// blank or non-numeric operand makes the result blank rather than failing, and <c>sum</c>,
/// <c>min</c> and <c>max</c> skip blanks. Mirrors <c>form-computed-expression.ts</c> in the builder.
/// </summary>
public sealed class FormExpression
{
    public const int MaxLength = 500;
    public const int MaxDepth = 32;
    public const int MaxTokens = 200;

    private static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        "sum", "min", "max", "round", "abs", "coalesce", "concat",
    };

    private readonly Node _root;

    private FormExpression(Node root, IReadOnlyList<string> fields)
    {
        _root = root;
        ReferencedFields = fields;
    }

    /// <summary>Every data name the expression reads, first-seen first.</summary>
    public IReadOnlyList<string> ReferencedFields { get; }

    /// <summary>Parses an expression; throws <see cref="FormExpressionException"/> saying where it went wrong.</summary>
    public static FormExpression Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormExpressionException(0, "The expression is empty.");
        }

        if (text.Length > MaxLength)
        {
            throw new FormExpressionException(MaxLength, $"The expression is longer than {MaxLength} characters.");
        }

        var tokens = Tokenize(text);
        var parser = new Parser(tokens);
        var root = parser.ParseExpression(0);
        parser.Expect(TokenKind.End);

        return new FormExpression(root, parser.Fields);
    }

    /// <summary>Whether the text parses; the error when it does not.</summary>
    public static bool TryParse(string? text, out FormExpression? expression, out FormExpressionException? error)
    {
        try
        {
            expression = Parse(text);
            error = null;
            return true;
        }
        catch (FormExpressionException ex)
        {
            expression = null;
            error = ex;
            return false;
        }
    }

    /// <summary>The expression's value for one set of answers. Never throws: anything that cannot be worked out is blank.</summary>
    public FormExpressionValue Evaluate(IReadOnlyDictionary<string, object?> answers)
    {
        try
        {
            return _root.Evaluate(answers);
        }
        catch (Exception ex) when (ex is OverflowException or DivideByZeroException or ArithmeticException)
        {
            return FormExpressionValue.Blank;
        }
    }

    // ---- Tokens ----------------------------------------------------------------------------------

    private enum TokenKind
    {
        Number,
        Text,
        Name,
        Plus,
        Minus,
        Star,
        Slash,
        LeftParen,
        RightParen,
        Comma,
        End,
    }

    private readonly record struct Token(TokenKind Kind, string Text, int Position, decimal Number = 0);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (tokens.Count >= MaxTokens)
            {
                throw new FormExpressionException(i, $"The expression has more than {MaxTokens} parts.");
            }

            var start = i;

            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
            {
                while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '.'))
                {
                    i++;
                }

                var literal = text[start..i];
                if (!decimal.TryParse(literal, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                {
                    throw new FormExpressionException(start, $"'{literal}' is not a number.");
                }

                tokens.Add(new Token(TokenKind.Number, literal, start, number));
                continue;
            }

            if (c is '\'' or '"')
            {
                var quote = c;
                var value = new StringBuilder();
                i++;

                while (true)
                {
                    if (i >= text.Length)
                    {
                        throw new FormExpressionException(start, "A text is not closed.");
                    }

                    if (text[i] == quote)
                    {
                        // A doubled quote is the quote itself: 'it''s'.
                        if (i + 1 < text.Length && text[i + 1] == quote)
                        {
                            value.Append(quote);
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    value.Append(text[i]);
                    i++;
                }

                tokens.Add(new Token(TokenKind.Text, value.ToString(), start));
                continue;
            }

            if (char.IsAsciiLetter(c) || c == '_')
            {
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Name, text[start..i], start));
                continue;
            }

            var kind = c switch
            {
                '+' => TokenKind.Plus,
                '-' => TokenKind.Minus,
                '*' => TokenKind.Star,
                '/' => TokenKind.Slash,
                '(' => TokenKind.LeftParen,
                ')' => TokenKind.RightParen,
                ',' => TokenKind.Comma,
                _ => throw new FormExpressionException(i, $"'{c}' is not allowed here."),
            };

            tokens.Add(new Token(kind, c.ToString(), i));
            i++;
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, text.Length));
        return tokens;
    }

    // ---- Parser ----------------------------------------------------------------------------------

    private sealed class Parser(List<Token> tokens)
    {
        private int _index;

        public List<string> Fields { get; } = [];

        private Token Current => tokens[_index];

        public void Expect(TokenKind kind)
        {
            if (Current.Kind != kind)
            {
                throw Unexpected();
            }

            _index++;
        }

        public Node ParseExpression(int depth)
        {
            Guard(depth);
            var left = ParseTerm(depth + 1);

            while (Current.Kind is TokenKind.Plus or TokenKind.Minus)
            {
                var op = Current.Kind;
                _index++;
                var right = ParseTerm(depth + 1);
                left = new Binary(op == TokenKind.Plus ? '+' : '-', left, right);
            }

            return left;
        }

        private Node ParseTerm(int depth)
        {
            Guard(depth);
            var left = ParseUnary(depth + 1);

            while (Current.Kind is TokenKind.Star or TokenKind.Slash)
            {
                var op = Current.Kind;
                _index++;
                var right = ParseUnary(depth + 1);
                left = new Binary(op == TokenKind.Star ? '*' : '/', left, right);
            }

            return left;
        }

        private Node ParseUnary(int depth)
        {
            Guard(depth);

            if (Current.Kind == TokenKind.Minus)
            {
                _index++;
                return new Negate(ParseUnary(depth + 1));
            }

            if (Current.Kind == TokenKind.Plus)
            {
                _index++;
                return ParseUnary(depth + 1);
            }

            return ParsePrimary(depth + 1);
        }

        private Node ParsePrimary(int depth)
        {
            Guard(depth);
            var token = Current;

            switch (token.Kind)
            {
                case TokenKind.Number:
                    _index++;
                    return new Constant(FormExpressionValue.OfNumber(token.Number));

                case TokenKind.Text:
                    _index++;
                    return new Constant(FormExpressionValue.OfText(token.Text));

                case TokenKind.LeftParen:
                    _index++;
                    var inner = ParseExpression(depth + 1);
                    Expect(TokenKind.RightParen);
                    return inner;

                case TokenKind.Name when tokens[_index + 1].Kind == TokenKind.LeftParen:
                    return ParseCall(depth + 1);

                case TokenKind.Name:
                    _index++;
                    if (!Fields.Contains(token.Text, StringComparer.OrdinalIgnoreCase))
                    {
                        Fields.Add(token.Text);
                    }

                    return new Field(token.Text);

                default:
                    throw Unexpected();
            }
        }

        private Node ParseCall(int depth)
        {
            var name = Current;
            if (!Functions.Contains(name.Text))
            {
                throw new FormExpressionException(name.Position, $"'{name.Text}' is not a known function.");
            }

            _index += 2; // the name and "("
            var arguments = new List<Node>();

            if (Current.Kind != TokenKind.RightParen)
            {
                arguments.Add(ParseExpression(depth + 1));

                while (Current.Kind == TokenKind.Comma)
                {
                    _index++;
                    arguments.Add(ParseExpression(depth + 1));
                }
            }

            Expect(TokenKind.RightParen);

            var function = name.Text.ToLowerInvariant();
            var (min, max) = function switch
            {
                "abs" => (1, 1),
                "round" => (1, 2),
                _ => (1, int.MaxValue),
            };

            if (arguments.Count < min || arguments.Count > max)
            {
                throw new FormExpressionException(name.Position, $"'{name.Text}' does not take {arguments.Count} value(s).");
            }

            return new Call(function, arguments);
        }

        private void Guard(int depth)
        {
            if (depth > MaxDepth * 4)
            {
                throw new FormExpressionException(Current.Position, $"The expression nests deeper than {MaxDepth} levels.");
            }
        }

        private FormExpressionException Unexpected() =>
            Current.Kind == TokenKind.End
                ? new FormExpressionException(Current.Position, "The expression ends too early.")
                : new FormExpressionException(Current.Position, $"'{Current.Text}' is not expected here.");
    }

    // ---- Tree ------------------------------------------------------------------------------------

    private abstract record Node
    {
        public abstract FormExpressionValue Evaluate(IReadOnlyDictionary<string, object?> answers);
    }

    private sealed record Constant(FormExpressionValue Value) : Node
    {
        public override FormExpressionValue Evaluate(IReadOnlyDictionary<string, object?> answers) => Value;
    }

    private sealed record Field(string Name) : Node
    {
        public override FormExpressionValue Evaluate(IReadOnlyDictionary<string, object?> answers)
        {
            var answer = FormAnswerValue.Read(answers, Name);

            if (!answer.IsDefined || FormAnswerValues.IsBlank(answer))
            {
                return FormExpressionValue.Blank;
            }

            return answer.Value switch
            {
                JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetDecimal(out var d) => FormExpressionValue.OfNumber(d),
                decimal d => FormExpressionValue.OfNumber(d),
                double d when double.IsFinite(d) => FormExpressionValue.OfNumber((decimal)d),
                float f when float.IsFinite(f) => FormExpressionValue.OfNumber((decimal)f),
                int or long or short or byte => FormExpressionValue.OfNumber(Convert.ToDecimal(answer.Value, CultureInfo.InvariantCulture)),
                _ => FormExpressionValue.OfText(FormAnswerValues.AsText(answer)),
            };
        }
    }

    private sealed record Negate(Node Operand) : Node
    {
        public override FormExpressionValue Evaluate(IReadOnlyDictionary<string, object?> answers) =>
            Operand.Evaluate(answers).AsNumber() is decimal n ? FormExpressionValue.OfNumber(-n) : FormExpressionValue.Blank;
    }

    private sealed record Binary(char Op, Node Left, Node Right) : Node
    {
        public override FormExpressionValue Evaluate(IReadOnlyDictionary<string, object?> answers)
        {
            if (Left.Evaluate(answers).AsNumber() is not decimal a || Right.Evaluate(answers).AsNumber() is not decimal b)
            {
                return FormExpressionValue.Blank;
            }

            return Op switch
            {
                '+' => FormExpressionValue.OfNumber(a + b),
                '-' => FormExpressionValue.OfNumber(a - b),
                '*' => FormExpressionValue.OfNumber(a * b),
                '/' => b == 0 ? FormExpressionValue.Blank : FormExpressionValue.OfNumber(a / b),
                _ => FormExpressionValue.Blank,
            };
        }
    }

    private sealed record Call(string Function, IReadOnlyList<Node> Arguments) : Node
    {
        public override FormExpressionValue Evaluate(IReadOnlyDictionary<string, object?> answers)
        {
            var values = Arguments.Select(a => a.Evaluate(answers)).ToList();

            switch (Function)
            {
                case "sum":
                {
                    var numbers = values.Select(v => v.AsNumber()).OfType<decimal>().ToList();
                    return numbers.Count == 0 ? FormExpressionValue.Blank : FormExpressionValue.OfNumber(numbers.Sum());
                }

                case "min":
                {
                    var numbers = values.Select(v => v.AsNumber()).OfType<decimal>().ToList();
                    return numbers.Count == 0 ? FormExpressionValue.Blank : FormExpressionValue.OfNumber(numbers.Min());
                }

                case "max":
                {
                    var numbers = values.Select(v => v.AsNumber()).OfType<decimal>().ToList();
                    return numbers.Count == 0 ? FormExpressionValue.Blank : FormExpressionValue.OfNumber(numbers.Max());
                }

                case "abs":
                    return values[0].AsNumber() is decimal absolute ? FormExpressionValue.OfNumber(Math.Abs(absolute)) : FormExpressionValue.Blank;

                case "round":
                {
                    if (values[0].AsNumber() is not decimal n)
                    {
                        return FormExpressionValue.Blank;
                    }

                    var digits = values.Count > 1 && values[1].AsNumber() is decimal d ? (int)Math.Clamp(d, 0, 10) : 0;
                    return FormExpressionValue.OfNumber(Math.Round(n, digits, MidpointRounding.AwayFromZero));
                }

                case "coalesce":
                    return values.FirstOrDefault(v => !v.IsBlank) ?? FormExpressionValue.Blank;

                case "concat":
                    return FormExpressionValue.OfText(string.Concat(values.Select(v => v.AsText() ?? string.Empty)));

                default:
                    return FormExpressionValue.Blank;
            }
        }
    }
}

/// <summary>What an expression works out to: a number, a text, or blank.</summary>
public sealed record FormExpressionValue(decimal? Number, string? Text)
{
    public static readonly FormExpressionValue Blank = new(null, null);

    public static FormExpressionValue OfNumber(decimal value) => new(value, null);

    public static FormExpressionValue OfText(string? value) => string.IsNullOrEmpty(value) ? Blank : new(null, value);

    public bool IsBlank => Number is null && string.IsNullOrEmpty(Text);

    /// <summary>The value as a number: a number as is, a text when it reads as a plain decimal number, else blank.</summary>
    public decimal? AsNumber() =>
        Number ?? (decimal.TryParse(Text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null);

    /// <summary>The value as text: a number without trailing zeros, a text as is, else blank.</summary>
    public string? AsText() =>
        Number is decimal n ? n.ToString("0.############", CultureInfo.InvariantCulture) : Text;
}

/// <summary>An expression that does not parse, and where: <see cref="Position"/> is the zero-based character.</summary>
public sealed class FormExpressionException(int position, string message) : Exception(message)
{
    public int Position { get; } = position;
}
