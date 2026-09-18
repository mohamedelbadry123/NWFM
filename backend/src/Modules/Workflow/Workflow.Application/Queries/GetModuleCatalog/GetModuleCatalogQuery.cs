namespace Workflow.Application.Queries.GetModuleCatalog;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Constants;

public sealed record GetModuleCatalogQuery : IRequest<Result<IReadOnlyList<ModuleCatalogEntry>>>;
