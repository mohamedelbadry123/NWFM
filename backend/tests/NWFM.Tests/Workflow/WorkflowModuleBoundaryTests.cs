namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using System.Reflection;
using global::Workflow.Api.Controllers;
using global::Workflow.Application;
using global::Workflow.Infrastructure.Persistence;

/// <summary>
/// Enforces the layer-dependency rules for the Workflow module at test time so that
/// a mis-wired project reference fails the build pipeline rather than silently compiling.
/// </summary>
public sealed class WorkflowModuleBoundaryTests
{
    private static readonly Assembly DomainAssembly =
        typeof(global::Workflow.Domain.AssemblyMarker).Assembly;

    private static readonly Assembly ApplicationAssembly =
        typeof(AssemblyMarker).Assembly;

    private static readonly Assembly InfrastructureAssembly =
        typeof(WorkflowDbContext).Assembly;

    private static readonly Assembly ApiAssembly =
        typeof(WorkflowHealthController).Assembly;

    [Fact]
    public void WorkflowDomain_DoesNotReference_EfCore()
    {
        var refNames = DomainAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty);

        refNames.Should().NotContain(n =>
            n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorkflowDomain_DoesNotReference_AspNetCore()
    {
        var refNames = DomainAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty);

        refNames.Should().NotContain(n =>
            n.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorkflowDomain_DoesNotReference_AnyOtherBusinessModule()
    {
        var businessModules = new[]
        {
            "Identity", "Organization", "Consent", "DSAR", "Breach",
            "DPIA", "Vendor", "Complaints", "Audit", "Notifications",
            "DPO", "Ropa", "Purposes", "Retention", "PrivacyNotices",
            "DataSubject", "SuperAdmin"
        };

        var refNames = DomainAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty);

        foreach (var module in businessModules)
        {
            refNames.Should().NotContain(n =>
                n.Contains(module, StringComparison.OrdinalIgnoreCase),
                because: $"Workflow.Domain must not reference {module}");
        }
    }

    [Fact]
    public void WorkflowApplication_DoesNotReference_Infrastructure()
    {
        var refNames = ApplicationAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty);

        refNames.Should().NotContain(n =>
            n.Equals("Workflow.Infrastructure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorkflowApi_DoesNotReference_Infrastructure()
    {
        var refNames = ApiAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty);

        refNames.Should().NotContain(n =>
            n.Equals("Workflow.Infrastructure", StringComparison.OrdinalIgnoreCase),
            because: "Workflow.Api must not depend on Infrastructure — only Application");
    }

    [Fact]
    public void WorkflowApi_DoesNotReference_EfCore()
    {
        var refNames = ApiAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty);

        refNames.Should().NotContain(n =>
            n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorkflowInfrastructure_References_Application()
    {
        var refNames = InfrastructureAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty);

        refNames.Should().Contain("Workflow.Application");
    }
}
