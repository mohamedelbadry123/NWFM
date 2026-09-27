using FluentValidation;
using Tasks.Domain.Entities;

namespace Tasks.Application.Tasks.Common;

/// <summary>Where a task goes — shared by the commands that raise and move one.</summary>
public interface ITaskLocationInput
{
    double Latitude { get; }
    double Longitude { get; }
    string? Address { get; }
    string? CbuCode { get; }
    string? BranchCode { get; }
    string? OperationAreaCode { get; }
    string? DepartmentCode { get; }
}

/// <summary>The location rules, once for every command that takes one.</summary>
public sealed class TaskLocationValidator : AbstractValidator<ITaskLocationInput>
{
    public TaskLocationValidator()
    {
        // A task is where the work is: without a point there is nowhere to send a crew.
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).WithMessage("Latitude must be between -90 and 90.");
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).WithMessage("Longitude must be between -180 and 180.");
        RuleFor(x => x)
            .Must(x => x.Latitude != 0 || x.Longitude != 0)
            .WithName("Location")
            .WithMessage("Pick the task's location on the map.");

        RuleFor(x => x.Address).MaximumLength(FieldTask.AddressMaxLength);
        RuleFor(x => x.CbuCode).MaximumLength(FieldTask.OrgCodeMaxLength);
        RuleFor(x => x.BranchCode).MaximumLength(FieldTask.OrgCodeMaxLength);
        RuleFor(x => x.OperationAreaCode).MaximumLength(FieldTask.OrgCodeMaxLength);
        RuleFor(x => x.DepartmentCode).MaximumLength(FieldTask.OrgCodeMaxLength);
    }

    public static TaskLocation ToLocation(ITaskLocationInput input, string? departmentCode) =>
        new(
            input.Latitude,
            input.Longitude,
            input.Address,
            input.CbuCode,
            input.BranchCode,
            input.OperationAreaCode,
            departmentCode);
}
