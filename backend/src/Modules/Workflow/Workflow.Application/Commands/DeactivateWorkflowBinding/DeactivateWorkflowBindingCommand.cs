namespace Workflow.Application.Commands.DeactivateWorkflowBinding;

using MediatR;
using NWFM.Shared.Results;

public sealed record DeactivateWorkflowBindingCommand(Guid BindingId) : IRequest<Result<bool>>;
