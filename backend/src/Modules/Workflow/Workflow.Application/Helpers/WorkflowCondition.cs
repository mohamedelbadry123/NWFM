namespace Workflow.Application.Helpers;

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>Small, non-executable condition language: comparisons, &&, || and parentheses.</summary>
public static class WorkflowCondition
{
    private static readonly Regex Lex = new(@"\G\s*(>=|<=|==|!=|&&|\|\||[()<>]|'([^'\\]|\\.)*'|""([^""\\]|\\.)*""|-?\d+(?:\.\d+)?|[A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    public static bool IsValid(string expression)
    { try { Parse(expression); return true; } catch (FormatException) { return false; } }
    public static bool Evaluate(string expression, IReadOnlyDictionary<string, string?> values)
    { try { return Parse(expression)(values); } catch (FormatException) { return false; } }
    private static Func<IReadOnlyDictionary<string, string?>, bool> Parse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > 2000) throw new FormatException();
        var tokens = new List<string>(); var position = 0;
        while (position < expression.TrimEnd().Length)
        { var match = Lex.Match(expression, position); if (!match.Success) throw new FormatException(); tokens.Add(match.Groups[1].Value); position += match.Length; }
        var cursor = 0;
        bool Take(string token) { if (cursor < tokens.Count && tokens[cursor] == token) { cursor++; return true; } return false; }
        string Next() => cursor < tokens.Count ? tokens[cursor++] : throw new FormatException();
        Func<IReadOnlyDictionary<string, string?>, bool> Primary(int depth)
        {
            if (depth > 16) throw new FormatException();
            if (Take("(")) { var group = Or(depth + 1); if (!Take(")")) throw new FormatException(); return group; }
            var key = Next(); if (!Regex.IsMatch(key, @"^[A-Za-z_][A-Za-z0-9_.]*$")) throw new FormatException();
            var op = Next(); if (!new[] { "==", "!=", ">", "<", ">=", "<=" }.Contains(op)) throw new FormatException();
            var literal = Next(); if (new[] { "(", ")", "&&", "||", "==", "!=", ">", "<", ">=", "<=" }.Contains(literal)) throw new FormatException();
            object? expected = Literal(literal);
            return values => values.TryGetValue(key, out var raw) && Compare(Read(raw), expected, op);
        }
        Func<IReadOnlyDictionary<string, string?>, bool> And(int depth)
        { var result = Primary(depth); while (Take("&&")) { var left = result; var right = Primary(depth); result = values => left(values) && right(values); } return result; }
        Func<IReadOnlyDictionary<string, string?>, bool> Or(int depth)
        { var result = And(depth); while (Take("||")) { var left = result; var right = And(depth); result = values => left(values) || right(values); } return result; }
        var root = Or(0); if (cursor != tokens.Count) throw new FormatException(); return root;
    }
    private static object? Literal(string text)
    {
        if (text.StartsWith('\'')) return text[1..^1].Replace("\\'", "'").Replace("\\\\", "\\");
        if (text.StartsWith('"')) { try { return JsonSerializer.Deserialize<string>(text); } catch (JsonException) { throw new FormatException(); } }
        if (text == "null") return null;
        if (bool.TryParse(text, out var boolean)) return boolean;
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)) return number;
        return text;
    }
    private static object? Read(string? json)
    {
        if (json is null) return null;
        try { using var value = JsonDocument.Parse(json); return value.RootElement.ValueKind switch
        { JsonValueKind.String => value.RootElement.GetString(), JsonValueKind.Number when value.RootElement.TryGetDecimal(out var number) => number,
          JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Null => null, _ => json }; }
        catch (JsonException) { return json; }
    }
    private static bool Compare(object? actual, object? expected, string op)
    {
        int? order = (actual, expected) switch
        {
            (null, null) => 0,
            (decimal a, decimal b) => a.CompareTo(b),
            (bool a, bool b) => a.CompareTo(b),
            (string a, string b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase),
            _ => null
        };
        return op switch { "==" => order == 0, "!=" => order != 0, ">" => order > 0, "<" => order < 0, ">=" => order >= 0, "<=" => order <= 0, _ => false };
    }
}
