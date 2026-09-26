using FluentValidation;

namespace FormEngine.Application.Forms.Queries.GetForms;

public sealed class GetFormsQueryValidator : AbstractValidator<GetFormsQuery>
{
    public const int MaxPageSize = 100;

    public GetFormsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0).WithMessage("Page number must be greater than 0.");
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, MaxPageSize)
            .WithMessage($"Page size must be between 1 and {MaxPageSize}.");
    }
}
