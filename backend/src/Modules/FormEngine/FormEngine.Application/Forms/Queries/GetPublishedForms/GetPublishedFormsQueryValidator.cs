using FluentValidation;

namespace FormEngine.Application.Forms.Queries.GetPublishedForms;

public sealed class GetPublishedFormsQueryValidator : AbstractValidator<GetPublishedFormsQuery>
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;

    public GetPublishedFormsQueryValidator()
    {
        RuleFor(x => x.Take)
            .InclusiveBetween(1, MaxTake)
            .WithMessage($"Take must be between 1 and {MaxTake}.");
    }
}
