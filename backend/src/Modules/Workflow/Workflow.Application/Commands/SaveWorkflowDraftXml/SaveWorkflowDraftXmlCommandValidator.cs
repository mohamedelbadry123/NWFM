namespace Workflow.Application.Commands.SaveWorkflowDraftXml;

using FluentValidation;

public sealed class SaveWorkflowDraftXmlCommandValidator
    : AbstractValidator<SaveWorkflowDraftXmlCommand>
{
    private const int MaxXmlBytes = 512 * 1024;

    public SaveWorkflowDraftXmlCommandValidator()
    {
        RuleFor(x => x.VersionId).NotEmpty();
        RuleFor(x => x.XmlContent)
            .NotEmpty()
            .Must(xml => System.Text.Encoding.UTF8.GetByteCount(xml) <= MaxXmlBytes)
            .WithMessage("XML content exceeds the 512 KB limit.");
    }
}
