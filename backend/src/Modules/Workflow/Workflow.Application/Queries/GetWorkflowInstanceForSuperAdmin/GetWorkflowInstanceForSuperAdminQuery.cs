namespace Workflow.Application.Queries.GetWorkflowInstanceForSuperAdmin;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetWorkflowInstanceForSuperAdminQuery(Guid InstanceId)
    : IRequest<Result<WorkflowProgressDto>>;
