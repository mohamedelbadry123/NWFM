namespace Workflow.Application.Commands.ActivateWorkflowBinding;

using MediatR;
using NWFM.Shared.Results;

public sealed record ActivateWorkflowBindingCommand(Guid BindingId) : IRequest<Result<bool>>;
