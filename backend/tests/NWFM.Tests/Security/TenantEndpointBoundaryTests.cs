using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace NWFM.Tests.Security;

/// <summary>
/// New controllers require classification; every discovered action must cross
/// authorization unless its exact anonymous entry point is explicitly reviewed.
/// </summary>
public sealed class TenantEndpointBoundaryTests
{
    private static readonly Dictionary<string, string> AnonymousActions = new()
    {
        ["AppContextController.Get"] = "Public deployment name/ID; participant data is restricted to the verified current user.",
        ["AuthController.Login"] = "Password authentication establishes a tenant-bound session.",
        ["AuthController.Refresh"] = "High-entropy refresh credential is tenant/stamp-bound and consumed once.",
        ["AuthController.SsoStatus"] = "Public sign-in configuration, with no tenant-owned records.",
        ["AuthController.ExchangeSsoCode"] = "Tenant-bound expiring SSO code is consumed once before token issuance.",
        ["AuthController.TeamLogin"] = "Password authentication precedes the tenant-isolated OTP challenge.",
        ["AuthController.VerifyTeamOtp"] = "Tenant-isolated OTP and current team/account authorization precede token issuance.",
        ["AuthController.ResendTeamOtp"] = "Existing tenant-isolated challenge and bounded retry rules are required.",
        ["AuthSsoController.Login"] = "Starts the configured SAML identity-provider handshake.",
        ["AuthSsoController.Callback"] = "Validates the protected temporary SAML cookie and current account membership.",
        ["AuthSsoController.Logout"] = "Clears temporary cookies; account session revocation requires a verified user.",
        ["WorkflowIntegrationsController.Receive"] = "Connection/tenant-bound HMAC, timestamp and replay checks authenticate ingress."
    };

    [Fact]
    public void ControllersAndAnonymousEntryPointsCannotAppearWithoutClassification()
    {
        var controllers = Controllers();
        string[] classifiedControllers =
        [
            "AppContextController", "AdminController", "AuthController", "AuthSsoController", "LookupsController",
            "TeamsController", "UsersController", "FieldCatalogController", "FormSubmissionsController",
            "FormUploadsController", "FormVersionsController", "FormsController", "C2mActionMappingsController",
            "TaskTypesController", "TasksController", "BusinessCalendarsController", "SlaPoliciesController",
            "WorkflowActionsCatalogController", "WorkflowAssignmentGroupsController", "WorkflowBindingsCatalogController",
            "WorkflowBindingsController", "WorkflowDefinitionsController", "WorkflowDepartmentsController",
            "WorkflowHealthController", "WorkflowIncidentsController", "WorkflowIntegrationMessagesController",
            "WorkflowIntegrationsController", "WorkflowModuleCatalogController", "WorkflowNotificationsController",
            "WorkflowParticipantsController", "WorkflowRequestsController", "WorkflowRuntimeController",
            "WorkflowVersionsController", "WorkflowWorkloadController", "WorkflowWorkspaceController",
            "WorkItemsController", "WorkspaceActivityFormsController", "WorkspaceSlaController"
        ];
        Assert.Equal(classifiedControllers.Order(), controllers.Select(x => x.Name).Order());

        var observedAnonymous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var controller in controllers)
        {
            var methods = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(x => !x.IsSpecialName && !x.IsDefined(typeof(NonActionAttribute), true));
            Assert.NotEmpty(methods);
            foreach (var method in methods)
            {
                var name = $"{controller.Name}.{method.Name}";
                Assert.True(method.GetCustomAttributes<HttpMethodAttribute>(true).Any(),
                    $"{name} must explicitly declare its HTTP method before it is exposed.");
                var anonymous = controller.IsDefined(typeof(AllowAnonymousAttribute), true) ||
                    method.IsDefined(typeof(AllowAnonymousAttribute), true);
                if (anonymous)
                {
                    Assert.True(AnonymousActions.ContainsKey(name), $"Unreviewed anonymous action: {name}");
                    observedAnonymous.Add(name);
                    continue;
                }
                Assert.True(controller.GetCustomAttributes<AuthorizeAttribute>(true).Any() ||
                    method.GetCustomAttributes<AuthorizeAttribute>(true).Any(),
                    $"{name} has no authenticated authorization boundary.");
            }
        }
        Assert.Equal(AnonymousActions.Keys.Order(), observedAnonymous.Order());
        Assert.All(AnonymousActions.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
    }

    private static Type[] Controllers()
    {
        Assembly[] modules =
        [
            typeof(NWFM.Api.Controllers.AppContextController).Assembly,
            typeof(Auth.Api.Controllers.AuthController).Assembly,
            typeof(FormEngine.Api.Controllers.FormsController).Assembly,
            typeof(Tasks.Api.Controllers.TasksController).Assembly,
            typeof(global::Workflow.Api.Controllers.WorkItemsController).Assembly
        ];
        return modules.SelectMany(x => x.GetTypes())
            .Where(x => !x.IsAbstract && typeof(ControllerBase).IsAssignableFrom(x)).ToArray();
    }
}
