using FluentValidation;

namespace FormEngine.Application.Forms.Queries.GetFormVersion;

public sealed class GetFormVersionQueryValidator : AbstractValidator<GetFormVersionQuery>
{
    public GetFormVersionQueryValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty().WithMessage("Form id is required.");
        RuleFor(x => x.VersionNo)
            .GreaterThan(0).WithMessage("Version number must be greater than 0.")
            .When(x => x.VersionNo.HasValue);
    }
}
