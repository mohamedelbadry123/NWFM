using System.Text.Json;
using System.Xml.Linq;
using Auth.Domain.Entities.Lookups;
using Auth.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Integration.Workflow;
using Workflow.Application.Commands.CreateWorkflowDraft;
using Workflow.Application.Commands.PublishWorkflowVersion;
using Workflow.Application.Commands.SaveWorkflowDraftXml;
using Workflow.Application.Integrations;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Infrastructure.Persistence;

namespace NWFM.Api.Services;

/// <summary>Explicit, additive demonstration data. Never updates an existing definition or lookup.</summary>
public static class WorkflowWorkspaceDemo
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("WorkflowDemo:Enabled")) return;
        if (!configuration.GetValue<bool>("WorkflowSettings:AllowPrivateConnections"))
            throw new InvalidOperationException("WorkflowDemo requires WorkflowSettings:AllowPrivateConnections for its loopback transports.");
        var db = services.GetRequiredService<WorkflowDbContext>();
        var auth = services.GetRequiredService<AuthDbContext>();
        var tenant = services.GetRequiredService<ICurrentTenant>().OrganizationId;
        var references = services.GetRequiredService<IWorkflowReferenceData>();
        var integrations = services.GetRequiredService<IWorkflowIntegrations>();
        var workspace = services.GetRequiredService<IWorkflowWorkspace>();
        var engine = services.GetRequiredService<Workflow.Application.Abstractions.IWorkflowRuntimeEngine>();
        var sender = services.GetRequiredService<ISender>();
        var now = DateTime.UtcNow;
        var admin = await auth.Users.Where(u => u.UserName == "administrator@localhost").Select(u => u.Id).FirstOrDefaultAsync();
        if (admin == Guid.Empty) throw new InvalidOperationException("Create the local administrator before enabling WorkflowDemo.");

        var department = await auth.Departments.FirstOrDefaultAsync(d => d.NameEn == "Water Network" && d.IsActive);
        if (department is null)
        {
            department = Department.Create("DEMO-WATER", "Water Network", "شبكة المياه");
            auth.Departments.Add(department); await auth.SaveChangesAsync();
        }
        foreach (var (code, name) in new[] { ("ISOLATION", "Isolation"), ("DEMO-REVIEW", "Demo — Request review"), ("DEMO-CLOSURE", "Demo — Closure") })
            if (!await auth.FieldActivityTypes.AnyAsync(f => f.DepartmentCode == department.Code && f.Code == code))
                auth.FieldActivityTypes.Add(FieldActivityType.Create(code, name, code == "ISOLATION" ? "عزل" : "تجريبي — " + name, department.Code));
        await auth.SaveChangesAsync();
        WorkflowGeography? geography = null;
        foreach (var cluster in await references.ListAsync("clusters", null, default))
        {
            foreach (var region in await references.ListAsync("regions", cluster.Code, default))
            {
                var city = (await references.ListAsync("cities", region.Code, default)).FirstOrDefault();
                if (city is not null) { geography = new(cluster.Code, region.Code, city.Code); break; }
            }
            if (geography is not null) break;
        }
        if (geography is null) throw new InvalidOperationException("WorkflowDemo needs one active cluster/region/city chain in Auth lookups.");
        var groups = new List<WorkflowAssignmentGroup>();
        var actors = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var user = Guid.Parse($"30000000-0000-0000-0000-00000000000{i + 1}");
            var participant = await db.Participants.FirstOrDefaultAsync(p => p.UserId == user);
            if (participant is null)
            {
                participant = WorkflowParticipant.Create(tenant, user, new[] { "Demo reviewer", "Demo isolation engineer", "Demo closure officer" }[i], $"demo-{i}@example.test", now);
                participant.MarkDemo(); db.Participants.Add(participant);
            }
            if (!participant.IsDemo) throw new InvalidOperationException("A reserved demo user identifier is already used by a business participant.");
            var code = "DEMO-WORKSPACE-" + i;
            var group = await db.AssignmentGroups.FirstOrDefaultAsync(g => g.Code == code);
            if (group is null)
            {
                group = WorkflowAssignmentGroup.Create(tenant, code, new[] { "Demo — Review team", "Demo — Isolation team", "Demo — Closure team" }[i], AssignmentStrategy.RoundRobin, now);
                db.AssignmentGroups.Add(group);
            }
            if (!await db.GroupMembers.AnyAsync(m => m.AssignmentGroupId == group.Id && m.ParticipantId == participant.Id))
                db.GroupMembers.Add(WorkflowGroupMember.Create(group.Id, participant.Id, true, true, now));
            groups.Add(group); actors.Add(user);
        }
        await db.SaveChangesAsync();
        async Task<ConnectionDto> Connection(ConnectionInput input)
        {
            var existing = (await integrations.ListConnectionsAsync(default)).FirstOrDefault(c => c.Name == input.Name);
            if (existing is not null) return existing;
            var result = await integrations.SaveConnectionAsync(null, input, default);
            if (result.IsFailure) throw new InvalidOperationException(result.Error.Message);
            return result.Value;
        }
        var http = await Connection(new("Demo — Local HTTP", "Http", "http://127.0.0.1:5091", "None", AllowPrivateNetwork: true));
        var smtp = await Connection(new("Demo — Local email", "Smtp", "127.0.0.1", "None", new() { ["fromAddress"] = "workflow@example.test" }, true, 2525, false));
        var callbackKey = configuration["WorkflowDemo:CallbackKey"];
        if (string.IsNullOrWhiteSpace(callbackKey) || callbackKey.Length < 32)
            throw new InvalidOperationException("Set WorkflowDemo:CallbackKey to a local demo credential of at least 32 characters.");
        var webhook = await Connection(new("Demo — Callback", "Webhook", "demo.callback", "ApiKey", new() { ["apiKey"] = callbackKey }));
        XNamespace ns = "https://privora.io/workflow/v1";
        XElement Node(string key, string type, string name, object? config = null, Guid? group = null) => new(ns + "Activity",
            new XAttribute("nodeKey", key), new XAttribute("type", type), new XAttribute("name", name),
            new XAttribute("positionX", key == "start" ? 60 : key == "end" ? 1150 : 260), new XAttribute("positionY", 150),
            config is null ? null : new XAttribute("configurationJson", JsonSerializer.Serialize(config, IntegrationJson.Options)),
            group is null ? null : new XAttribute("assignmentGroupId", group), group is null ? null : new XElement(ns + "Outcomes",
                new XElement(ns + "Outcome", new XAttribute("key", "APPROVE"), new XAttribute("name", "Approve"), new XAttribute("isDefault", "true")),
                new XElement(ns + "Outcome", new XAttribute("key", "REJECT"), new XAttribute("name", "Reject"), new XAttribute("requiresComment", "true"))));
        XElement Edge(string from, string to) => new(ns + "Transition", new XAttribute("key", from + "-" + to), new XAttribute("from", from), new XAttribute("to", to));
        async Task<(WorkflowDefinition Definition, WorkflowVersion Version)> Publish(string key, string name, WorkflowWorkspaceDefinition settings, List<XElement> nodes)
        {
            var existing = await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.DefinitionKey == key);
            if (existing is not null)
            {
                var published = await db.WorkflowVersions.Where(v => v.WorkflowDefinitionId == existing.Id && v.Status == WorkflowVersionStatus.Published).OrderByDescending(v => v.VersionNumber).FirstOrDefaultAsync();
                if (published is null) throw new InvalidOperationException($"Demo definition {key} already exists without a published version; inspect it before reseeding.");
                return (existing, published);
            }
            var definition = WorkflowDefinition.Create(tenant, key, name, now);
            db.WorkflowDefinitions.Add(definition); await db.SaveChangesAsync();
            var draft = await sender.Send(new CreateWorkflowDraftCommand(definition.Id, admin));
            if (draft.IsFailure) throw new InvalidOperationException(draft.Error.Message);
            var graph = new[] { Node("start", "Start", "Start") }.Concat(nodes).Append(Node("end", "End", "Completed")).ToList();
            for (var index = 0; index < graph.Count; index++) graph[index].SetAttributeValue("positionX", 60 + 260 * index);
            var xml = new XElement(ns + "Workflow", new XAttribute("workspaceJson", JsonSerializer.Serialize(settings, IntegrationJson.Options)),
                new XElement(ns + "Activities", graph), new XElement(ns + "Transitions", graph.Zip(graph.Skip(1), (a, b) => Edge(a.Attribute("nodeKey")!.Value, b.Attribute("nodeKey")!.Value))));
            var saved = await sender.Send(new SaveWorkflowDraftXmlCommand(draft.Value.Id, xml.ToString()));
            if (saved.IsFailure) throw new InvalidOperationException(saved.Error.Message);
            var publishedResult = await sender.Send(new PublishWorkflowVersionCommand(draft.Value.Id, admin));
            if (publishedResult.IsFailure) throw new InvalidOperationException(publishedResult.Error.Message);
            return (definition, (await db.WorkflowVersions.FindAsync(draft.Value.Id))!);
        }
        var names = new[] { "Request Review", "Isolation Execution", "Closure" };
        var fa = new[] { "DEMO-REVIEW", "ISOLATION", "DEMO-CLOSURE" };
        var children = new List<(WorkflowDefinition Definition, WorkflowVersion Version)>();
        for (var i = 0; i < 3; i++)
            children.Add(await Publish("DEMO-CHILD-" + i, "Demo — " + names[i] + " steps", new("Child"), [Node("review", "UserTask", names[i] + " checklist", new { departmentCode = department.Code, fieldActivityCode = fa[i], slaDurationHours = 8, rejectTargetNodeKey = "review" }, groups[i].Id)]));
        object Event(string id, string trigger, string kind, bool required, object config) => new { id, name = "Demo — " + id, trigger, kind, required, configuration = config };
        async Task<(WorkflowDefinition Definition, WorkflowVersion Version)> Main(string suffix, double hours = 24, bool failure = false, bool callback = false)
        {
            var nodes = new List<XElement>();
            for (var i = 0; i < 3; i++)
            {
                var activityEvents = new List<object>();
                if (i == 2)
                {
                    activityEvents.Add(Event("rest", "OnApprove", "Http", true, new { connectionId = http.Id, method = "POST", path = failure ? "/fail" : "/rest", body = "{\"reference\":\"{{CorrelationId}}\"}", maxAttempts = 1, outputMappings = new { externalId = "body.id" } }));
                    activityEvents.Add(Event("soap", "OnComplete", "Soap", true, new { connectionId = http.Id, protocol = "Soap", method = "POST", path = "/soap", soapVersion = "1.1", soapAction = "urn:demo:Complete", body = "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body><Complete xmlns=\"urn:demo\"><Reference>{{CorrelationId}}</Reference></Complete></soap:Body></soap:Envelope>", xmlNamespaces = new Dictionary<string, string> { ["d"] = "urn:demo" }, xmlOutputMappings = new { soapResult = "//d:Result" } }));
                    activityEvents.Add(Event("email", "OnComplete", "Email", false, new { connectionId = smtp.Id, channels = "Email", to = "reviewer@example.test", subject = "Demo workflow completed", body = "Request {{CorrelationId}} completed.", failurePolicy = "Retry" }));
                    activityEvents.Add(Event("sms", "OnComplete", "Sms", false, new { connectionId = http.Id, protocol = "Sms", method = "POST", path = "/sms", smsTo = "+966500000000", smsMessage = "Demo {{CorrelationId}} completed", body = "{\"to\":\"{{smsTo}}\",\"message\":\"{{smsMessage}}\"}", outputMappings = new { providerMessageId = "body.id" } }));
                }
                activityEvents.Add(Event("sla", "OnSlaBreach", "Email", false, new { connectionId = smtp.Id, channels = "Email", to = "supervisor@example.test", subject = "Demo SLA overdue", body = names[i] + " is overdue.", failurePolicy = "Retry" }));
                nodes.Add(Node("stage-" + i, "MainActivity", names[i], new { departmentCode = department.Code, fieldActivityCode = fa[i], slaDurationHours = hours, definitionKey = children[i].Definition.DefinitionKey, versionId = children[i].Version.Id, rejectTargetNodeKey = "stage-" + Math.Max(0, i - 1), events = activityEvents }, groups[i].Id));
            }
            if (callback) nodes.Insert(1, Node("callback", "WaitEvent", "Wait for field confirmation", new { connectionId = webhook.Id, eventKey = "demo.field.completed", correlationVariable = "CorrelationId", timeoutSeconds = 86400 }));
            return await Publish("DEMO-MAIN-" + suffix, "Demo — Water isolation" + (suffix == "STANDARD" ? "" : " — " + suffix), new("Main", geography.ClusterCode, geography.RegionCode, geography.CityCode), nodes);
        }
        var main = await Main("STANDARD");
        var overdue = await Main("OVERDUE", .001);
        var failed = await Main("FAILURE", failure: true);
        var callbackMain = await Main("CALLBACK", callback: true);
        foreach (var scenario in new[] { ("Child execution", main, 0, false), ("Pending main approval", main, 1, false), ("Rework", main, 2, true), ("Completion", main, 6, false), ("Overdue SLA", overdue, 0, false), ("Integration failure", failed, 6, false), ("Callback waiting", callbackMain, 2, false) })
        {
            var reference = "Demo — " + scenario.Item1;
            if (await db.WorkflowInstances.AnyAsync(i => i.IsDemo && i.CorrelationId == reference)) continue;
            var started = await workspace.StartAsync(scenario.Item2.Definition.Id, new(Guid.NewGuid(), reference, true), admin, true, default);
            if (started.IsFailure) throw new InvalidOperationException(started.Error.Message);
            for (var step = 0; step < scenario.Item3; step++)
            {
                var detail = await workspace.GetAsync(started.Value, admin, true, null, default);
                var task = detail.Value.Tree.SelectMany(t => t.Activities).SelectMany(a => a.Tasks).Select(t => t.Task).FirstOrDefault(t => t.Status is WorkItemStatus.Pending or WorkItemStatus.Claimed);
                if (task is null) break;
                var effective = actors[groups.FindIndex(g => g.Id == task.AssignmentGroupId)];
                var acted = await workspace.ActAsync(task.Id, new(Guid.NewGuid(), scenario.Item4 && step == scenario.Item3 - 1 ? "REJECT" : "APPROVE", "Seeded demo scenario", DemoActorId: effective), admin, true, default);
                if (acted.IsFailure) throw new InvalidOperationException(acted.Error.Message);
                // The normal durable worker does this after startup. Seed scenarios
                // use the same engine operation before moving to their next action.
                var completedChild = await db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == task.WorkflowInstanceId && i.ParentInstanceId != null && i.Status == WorkflowInstanceStatus.Completed);
                if (completedChild is not null) await engine.ResumeFromCallActivityAsync(completedChild.ParentInstanceId!.Value, completedChild.ParentActivityNodeKey!, DateTime.UtcNow);
            }
        }
    }
}
