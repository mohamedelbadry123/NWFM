using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Workflow.Domain.Entities;

namespace Workflow.Application.Routing;

public static class RoutingDraftRevision
{
    // Presentation changes also participate in stale-session protection.
    public static string Of(WorkflowVersion version) => string.IsNullOrEmpty(version.XmlContent) ? ""
        : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { version.XmlContent, version.DesignerJson, version.WorkspaceJson }))));
}
