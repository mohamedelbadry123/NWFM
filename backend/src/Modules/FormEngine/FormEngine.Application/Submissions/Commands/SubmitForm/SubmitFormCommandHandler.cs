using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Submissions.Models;
using MediatR;
using NWFM.Shared.Results;

namespace FormEngine.Application.Submissions.Commands.SubmitForm;

/// <summary>The submit endpoint's slice. The work lives in <see cref="IFormSubmissionService"/>, shared with the form gateway.</summary>
public sealed class SubmitFormCommandHandler(IFormSubmissionService submissions)
    : IRequestHandler<SubmitFormCommand, Result<FormSubmissionCreatedDto>>
{
    public Task<Result<FormSubmissionCreatedDto>> Handle(SubmitFormCommand request, CancellationToken ct) =>
        submissions.SubmitAsync(
            new FormSubmissionRequest
            {
                FormDefinitionId = request.FormDefinitionId,
                VersionNo = request.VersionNo,
                ContextType = request.ContextType,
                ContextId = request.ContextId,
                ClientSubmissionId = request.ClientSubmissionId,
                ClientFilledAt = request.ClientFilledAt,
                Answers = request.Answers,
            },
            ct);
}
