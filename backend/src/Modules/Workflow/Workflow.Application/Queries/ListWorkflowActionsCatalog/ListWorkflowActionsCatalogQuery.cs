namespace Workflow.Application.Queries.ListWorkflowActionsCatalog;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ListWorkflowActionsCatalogQuery
    : IRequest<Result<IReadOnlyList<WorkflowActionCatalogEntryDto>>>;
