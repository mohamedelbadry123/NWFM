namespace NWFM.Shared.Integration.Workflow;

/// <summary>
/// Describes a business field available for start conditions and input mappings.
/// </summary>
public sealed record WorkflowFieldDescriptor(
    string FieldKey,
    string NameEn,
    string NameAr,
    string DataType);
