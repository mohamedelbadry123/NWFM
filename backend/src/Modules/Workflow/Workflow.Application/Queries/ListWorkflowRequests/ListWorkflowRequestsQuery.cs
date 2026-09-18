namespace Workflow.Application.Queries.ListWorkflowRequests;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;
using Workflow.Domain.Enums;

public sealed record ListWorkflowRequestsQuery(
    Guid OrganizationId,
    int PageNumber = 1,
    int PageSize = 20,
    string? Search = null,
    WorkflowInstanceStatus? Status = null,
    string? Service = null,
    string? CurrentStep = null,
    Guid? OriginalGroupId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? SlaStatus = null,
    string? SortBy = null) : IRequest<Result<WorkflowRequestPageDto>>;

public sealed record WorkflowRequestPageDto(
    IReadOnlyList<WorkflowRequestDto> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    WorkflowRequestKpiDto Kpis);
