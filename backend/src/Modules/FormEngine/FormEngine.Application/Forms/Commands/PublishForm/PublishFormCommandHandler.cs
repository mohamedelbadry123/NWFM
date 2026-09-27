using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Forms.Common;
using FormEngine.Application.Forms.Models;
using MediatR;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Results;

namespace FormEngine.Application.Forms.Commands.PublishForm;

public sealed class PublishFormCommandHandler(IFormPublisher publisher, ICurrentUser user)
    : IRequestHandler<PublishFormCommand, Result<FormDetailDto>>
{
    public async Task<Result<FormDetailDto>> Handle(PublishFormCommand request, CancellationToken ct)
    {
        var published = await publisher.PublishAsync(request.Id, user.Id, ct);

        return published.IsSuccess
            ? Result.Success(published.Value.ToDetailDto())
            : Result.Failure<FormDetailDto>(published.Error);
    }
}
