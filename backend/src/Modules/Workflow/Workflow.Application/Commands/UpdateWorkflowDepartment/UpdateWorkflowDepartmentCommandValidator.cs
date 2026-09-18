namespace Workflow.Application.Commands.UpdateWorkflowDepartment;

using FluentValidation;

public sealed class UpdateWorkflowDepartmentCommandValidator
    : AbstractValidator<UpdateWorkflowDepartmentCommand>
{
    public UpdateWorkflowDepartmentCommandValidator()
    {
        RuleFor(x => x.DepartmentId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameAr).MaximumLength(200).When(x => x.NameAr is not null);
        RuleFor(x => x.Code).MaximumLength(50).When(x => x.Code is not null);
    }
}
