using FluentValidation;

namespace Auth.Application.Permissions.Commands.AssignRolePermissions;

public sealed class AssignRolePermissionsCommandValidator : AbstractValidator<AssignRolePermissionsCommand>
{
    public AssignRolePermissionsCommandValidator()
    {
        RuleFor(x => x.RoleName)
            .NotEmpty()
            .WithMessage("Role name is required.");

        RuleFor(x => x.PermissionCodes)
            .NotNull()
            .WithMessage("Permission codes list must not be null.");
    }
}
