using FluentValidation;

namespace FormEngine.Application.FieldCatalog.Queries.GetFieldCatalogPaged;

public sealed class GetFieldCatalogPagedQueryValidator : AbstractValidator<GetFieldCatalogPagedQuery>
{
    public const int MaxPageSize = 200;

    public GetFieldCatalogPagedQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0).WithMessage("Page number must be greater than 0.");
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, MaxPageSize)
            .WithMessage($"Page size must be between 1 and {MaxPageSize}.");
    }
}
