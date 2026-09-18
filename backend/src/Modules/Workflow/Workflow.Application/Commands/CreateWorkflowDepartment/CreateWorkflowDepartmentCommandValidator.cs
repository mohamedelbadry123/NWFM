namespace Workflow.Application.Commands.CreateWorkflowDepartment;

using FluentValidation;

public sealed class CreateWorkflowDepartmentCommandValidator
    : AbstractValidator<CreateWorkflowDepartmentCommand>
{
    public CreateWorkflowDepartmentCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameAr).MaximumLength(200).When(x => x.NameAr is not null);
        RuleFor(x => x.Code).MaximumLength(50).When(x => x.Code is not null);
    }
}
