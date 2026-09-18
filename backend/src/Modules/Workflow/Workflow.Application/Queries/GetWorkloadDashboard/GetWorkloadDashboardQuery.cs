namespace Workflow.Application.Queries.GetWorkloadDashboard;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetWorkloadDashboardQuery(Guid OrganizationId)
    : IRequest<Result<WorkloadDashboardDto>>;
