using System.Text.RegularExpressions;
using FormEngine.Application.Common.Schema.Expressions;
using FormEngine.Domain.Constants;

namespace FormEngine.Application.Common.Schema;

/// <summary>
/// Checks a form's computed columns before the schema is saved or published: every key well formed
/// and unique, both labels given, a known output type, every rule with conditions, and every
/// expression and condition naming only fields the form has. Returns what is wrong, one message per
/// problem; empty when the columns are sound.
/// </summary>
public static partial class FormComputedColumnValidator
{
    public const int MaxColumns = 20;
    public const int MaxRules = 20;
    public const int KeyMaxLength = 64;
    public const int LabelMaxLength = 250;

    public static IReadOnlyList<string> Validate(FormSchema schema)
    {
        var errors = new List<string>();
        var columns = schema.ComputedColumns;

        if (columns.Count == 0)
        {
            return errors;
        }

        if (columns.Count > MaxColumns)
        {
            errors.Add($"A form can have at most {MaxColumns} computed columns.");
        }

        var fields = new HashSet<string>(schema.Fields.Select(f => f.DataName), StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            var name = string.IsNullOrWhiteSpace(column.Key) ? "(no key)" : column.Key;

            if (!KeyRegex().IsMatch(column.Key ?? string.Empty))
            {
                errors.Add($"Computed column '{name}': the key must start with a lower-case letter and hold only lower-case letters, digits and '_' (at most {KeyMaxLength}).");
            }
            else if (!keys.Add(column.Key!))
            {
                errors.Add($"Computed column '{name}' is declared more than once.");
            }

            if (string.IsNullOrWhiteSpace(column.LabelEn) || string.IsNullOrWhiteSpace(column.LabelAr))
            {
                errors.Add($"Computed column '{name}' needs an English and an Arabic label.");
            }
            else if (column.LabelEn.Length > LabelMaxLength || column.LabelAr.Length > LabelMaxLength)
            {
                errors.Add($"Computed column '{name}': a label can be at most {LabelMaxLength} characters.");
            }

            if (!FormComputedOutputTypes.IsDefined(column.OutputType))
            {
                errors.Add($"Computed column '{name}': the output type must be one of {string.Join(", ", FormComputedOutputTypes.All)}.");
            }

            if (column.Rules.Count > MaxRules)
            {
                errors.Add($"Computed column '{name}' can have at most {MaxRules} rules.");
            }

            if (column.Rules.Count == 0 && string.IsNullOrWhiteSpace(column.DefaultExpression))
            {
                errors.Add($"Computed column '{name}' needs a rule or a default value.");
            }

            for (var i = 0; i < column.Rules.Count; i++)
            {
                var rule = column.Rules[i];
                var where = $"Computed column '{name}', rule {i + 1}";

                if (!FormRuleEngine.HasConditions(rule.When))
                {
                    errors.Add($"{where}: a rule needs at least one condition.");
                }
                else
                {
                    foreach (var condition in rule.When!.Conditions.Where(c => !fields.Contains(c.Field)))
                    {
                        errors.Add($"{where}: the condition names '{condition.Field}', which is not a field of the form.");
                    }
                }

                CheckExpression(rule.Then, where, fields, errors);
            }

            if (!string.IsNullOrWhiteSpace(column.DefaultExpression))
            {
                CheckExpression(column.DefaultExpression, $"Computed column '{name}', default", fields, errors);
            }
        }

        return errors;
    }

    private static void CheckExpression(string? expression, string where, HashSet<string> fields, List<string> errors)
    {
        if (!FormExpression.TryParse(expression, out var parsed, out var error))
        {
            errors.Add($"{where}: {error!.Message} (at character {error.Position + 1})");
            return;
        }

        foreach (var field in parsed!.ReferencedFields.Where(f => !fields.Contains(f)))
        {
            errors.Add($"{where}: '{field}' is not a field of the form.");
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyRegex();
}
