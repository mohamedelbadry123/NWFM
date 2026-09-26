using FluentValidation;

namespace FormEngine.Application.FieldCatalog.Queries.GetFieldCatalog;

public sealed class GetFieldCatalogQueryValidator : AbstractValidator<GetFieldCatalogQuery>
{
    public const int DefaultTake = 20;
    public const int MaxTake = 100;

    public GetFieldCatalogQueryValidator()
    {
        RuleFor(x => x.Take)
            .InclusiveBetween(1, MaxTake)
            .WithMessage($"Take must be between 1 and {MaxTake}.");
    }
}
