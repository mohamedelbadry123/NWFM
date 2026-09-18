namespace Workflow.Application.Helpers;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Builds unique assignment-group codes from display names (A-Z, 0-9, _, -).
/// </summary>
public static partial class AssignmentGroupCodeGenerator
{
    public static string FromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "GROUP";

        var sb = new StringBuilder(name.Length);
        var prevSep = true;
        foreach (var ch in name.Trim().ToUpperInvariant())
        {
            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                sb.Append(ch);
                prevSep = false;
            }
            else if (!prevSep)
            {
                sb.Append('_');
                prevSep = true;
            }
        }

        var code = sb.ToString().Trim('_');
        if (string.IsNullOrEmpty(code))
            code = "GROUP";

        if (code.Length > 50)
            code = code[..50].TrimEnd('_');

        return code;
    }

    public static string WithSuffix(string baseCode, int suffix)
    {
        if (suffix <= 1)
            return baseCode.Length <= 50 ? baseCode : baseCode[..50];

        var suffixText = $"_{suffix}";
        var maxBase = Math.Max(1, 50 - suffixText.Length);
        var truncated = baseCode.Length <= maxBase ? baseCode : baseCode[..maxBase].TrimEnd('_');
        return truncated + suffixText;
    }

    [GeneratedRegex("^[A-Z0-9_-]+$")]
    public static partial Regex ValidCodePattern();
}
