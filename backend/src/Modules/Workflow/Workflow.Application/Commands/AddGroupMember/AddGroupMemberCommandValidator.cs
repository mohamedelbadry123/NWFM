namespace Workflow.Application.Commands.AddGroupMember;

using FluentValidation;

public sealed class AddGroupMemberCommandValidator : AbstractValidator<AddGroupMemberCommand>
{
    public AddGroupMemberCommandValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ParticipantId).NotEmpty();
        RuleFor(x => x.ValidTo)
            .GreaterThan(x => x.ValidFrom)
            .When(x => x.ValidFrom.HasValue && x.ValidTo.HasValue)
            .WithMessage("ValidTo must be after ValidFrom.");
    }
}
