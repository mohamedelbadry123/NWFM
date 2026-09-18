namespace Workflow.Application.Commands.SimulateWorkflowBinding;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record SimulateWorkflowBindingCommand(
    Guid BindingId,
    Guid OrganizationId,
    string? SamplePayloadJson)
    : IRequest<Result<WorkflowSimulationResultDto>>;
