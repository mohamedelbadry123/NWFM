using FluentValidation;
using FormEngine.Domain.Constants;

namespace FormEngine.Application.Submissions.Queries.GetFormSubmissions;

public sealed class GetFormSubmissionsQueryValidator : AbstractValidator<GetFormSubmissionsQuery>
{
    public const int MaxPageSize = 200;

    public GetFormSubmissionsQueryValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty().WithMessage("Form id is required.");
        RuleFor(x => x.PageNumber).GreaterThan(0).WithMessage("Page number must be greater than 0.");
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, MaxPageSize)
            .WithMessage($"Page size must be between 1 and {MaxPageSize}.");

        RuleFor(x => x.ContextType).MaximumLength(FormContextTypes.MaxLength);
        RuleFor(x => x.ContextId).MaximumLength(FormContextTypes.MaxLength);
    }
}
