namespace Workflow.Application.Commands.RegisterParticipant;

using FluentValidation;

public sealed class RegisterParticipantCommandValidator : AbstractValidator<RegisterParticipantCommand>
{
    public RegisterParticipantCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DisplayNameAr).MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.EmployeeNumber).MaximumLength(50).When(x => x.EmployeeNumber is not null);
    }
}
