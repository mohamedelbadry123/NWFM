namespace Workflow.Application.DTOs;

using Workflow.Domain.Enums;

public sealed record WorkflowVariableDefinitionDto(
    Guid Id,
    Guid WorkflowVersionId,
    string VariableKey,
    string Name,
    string? NameAr,
    VariableDataType DataType,
    bool IsRequired,
    bool IsSensitive,
    string? DefaultValue,
    string? Description,
    string? DescriptionAr);
