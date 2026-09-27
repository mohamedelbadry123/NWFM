using FluentValidation;
using FormEngine.Domain.Constants;
using FormEngine.Domain.Entities;

namespace FormEngine.Application.Forms.Common;

/// <summary>The input rules every form create/update/clone shares, in one place.</summary>
internal static class FormInputRules
{
    /// <summary>Letters, digits, and <c>- _ .</c> after the first character — a code ends up in URLs and file paths.</summary>
    public const string CodePattern = @"^[A-Za-z0-9][A-Za-z0-9_.\-]*$";

    public static IRuleBuilderOptions<T, string> ValidFormCode<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithMessage("Form code is required.")
            .MaximumLength(FormDefinition.CodeMaxLength)
            .WithMessage($"Form code must not exceed {FormDefinition.CodeMaxLength} characters.")
            .Matches(CodePattern)
            .WithMessage("Form code may contain only letters, digits, '-', '_' and '.', and must start with a letter or digit.");

    public static IRuleBuilderOptions<T, string> ValidFormName<T>(this IRuleBuilder<T, string> rule, string language) =>
        rule
            .NotEmpty().WithMessage($"{language} form name is required.")
            .MaximumLength(FormDefinition.NameMaxLength)
            .WithMessage($"{language} form name must not exceed {FormDefinition.NameMaxLength} characters.");

    public static IRuleBuilderOptions<T, string> ValidFormCategory<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithMessage("Form category is required.")
            .Must(FormCategories.IsDefined)
            .WithMessage($"Form category must be one of: {string.Join(", ", FormCategories.All)}.");

    public static IRuleBuilderOptions<T, string?> ValidDepartmentCode<T>(this IRuleBuilder<T, string?> rule) =>
        rule
            .MaximumLength(FormDefinition.DepartmentCodeMaxLength)
            .WithMessage($"Department code must not exceed {FormDefinition.DepartmentCodeMaxLength} characters.");
}
