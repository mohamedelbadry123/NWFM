namespace Workflow.Application.Commands.DeleteWorkflowBindingAssignmentMapping;

using MediatR;
using NWFM.Shared.Results;

public sealed record DeleteWorkflowBindingAssignmentMappingCommand(
    Guid MappingId,
    Guid OrganizationId) : IRequest<Result<bool>>;
