using FluentValidation;

namespace FormEngine.Application.Uploads.Commands.DeleteFormFile;

public sealed class DeleteFormFileCommandValidator : AbstractValidator<DeleteFormFileCommand>
{
    public DeleteFormFileCommandValidator()
    {
        RuleFor(x => x.FileId).NotEmpty().WithMessage("File id is required.");
    }
}
