namespace Workflow.Application.Abstractions;

using NWFM.Shared.Results;
using Workflow.Application.Models;

/// <summary>
/// Parses, validates, and canonicalizes Privora Workflow XML.
/// Implementation enforces all XML security requirements (no DTD, no external entities, size and depth limits).
/// Never executes XML content.
/// </summary>
public interface IWorkflowXmlCompiler
{
    /// <summary>
    /// Parses and schema-validates the XML. Returns a safe internal model and the canonical SHA-256 hash.
    /// Failure result contains the first security or structural error encountered.
    /// </summary>
    Result<WorkflowXmlDocument> Compile(string xmlContent, out string canonicalHash);
}
