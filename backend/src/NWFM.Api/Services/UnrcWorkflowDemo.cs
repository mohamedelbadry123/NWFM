using System.Text.Json;
using System.Xml.Linq;
using Auth.Domain.Entities.Lookups;
using Auth.Infrastructure.Persistence;
using FormEngine.Application.Common.Interfaces;
using FormEngine.Domain.Entities;
using FormEngine.Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Organization;
using Tasks.Domain.Entities;
using Tasks.Infrastructure.Persistence;
using Workflow.Application.Commands.CreateWorkflowDraft;
using Workflow.Application.Commands.PublishWorkflowVersion;
using Workflow.Application.Commands.SaveWorkflowDraftXml;
using Workflow.Application.Integrations;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Infrastructure.Persistence;

namespace NWFM.Api.Services;

/// <summary>An opt-in, local example. Never resets data or rewrites a published workflow.</summary>
internal static class UnrcWorkflowDemo
{
    internal const string MainKey = "UNRC-LOCAL";
    private const string DepartmentCode = "UNRC-DEMO";
    private const string ActivityCode = "UNRC";
    private static readonly XNamespace Ns = "https://privora.io/workflow/v1";
    private static string Json(object value) => JsonSerializer.Serialize(value, IntegrationJson.Options);

    internal static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!configuration.GetValue<bool>("UnrcDemo:Enabled")) return;
        var connection = new SqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"));
        var databaseHost = connection.DataSource.Split(',')[0].Trim();
        if (!environment.IsDevelopment() || databaseHost is not ("localhost" or "127.0.0.1" or "." or "(local)"))
            throw new InvalidOperationException("UNRC examples require Development and a local SQL Server.");
        if (!configuration.GetValue<bool>("WorkflowSettings:AllowPrivateConnections"))
            throw new InvalidOperationException("Enable WorkflowSettings:AllowPrivateConnections for the local UNRC simulator.");
        var db = services.GetRequiredService<WorkflowDbContext>();
        var auth = services.GetRequiredService<AuthDbContext>();
        var forms = services.GetRequiredService<FormEngineDbContext>();
        var tasks = services.GetRequiredService<TasksDbContext>();
        var sender = services.GetRequiredService<ISender>();
        var tenant = services.GetRequiredService<ICurrentTenant>().OrganizationId;
        var now = DateTime.UtcNow;
        var administrator = await auth.Users.Where(u => u.UserName == "administrator@localhost").Select(u => u.Id).SingleAsync();
        var existing = await db.WorkflowDefinitions.SingleOrDefaultAsync(d => d.DefinitionKey == MainKey);
        if (existing is not null && await db.WorkflowVersions.AnyAsync(v => v.WorkflowDefinitionId == existing.Id && v.Status == WorkflowVersionStatus.Published))
        {
            Console.WriteLine($"UNRC already exists: {existing.Id}. Its published configuration was preserved.");
            return;
        }

        if (!await auth.Departments.AnyAsync(d => d.Code == DepartmentCode))
            auth.Departments.Add(Department.Create(DepartmentCode, "UNRC local example", "مثال التوصيلة غير المسجلة"));
        if (!await auth.ActivitySources.AnyAsync(s => s.Code == "NWFM"))
            auth.ActivitySources.Add(ActivitySource.Create("NWFM", "NWFM", "NWFM", ActivitySourceKinds.Internal, null));
        if (!await auth.FieldActivityTypes.AnyAsync(f => f.Code == ActivityCode))
            auth.FieldActivityTypes.Add(FieldActivityType.Create(ActivityCode, "Unregistered Connection (UNRC)", "توصيلة غير مسجلة", ["NWFM"]));
        await auth.SaveChangesAsync();
        var calendar = await db.BusinessCalendars.SingleOrDefaultAsync(c => c.Code == "UNRC-LOCAL");
        if (calendar is null)
        {
            calendar = BusinessCalendar.Create("UNRC-LOCAL", "UNRC — Sunday to Thursday", "Asia/Riyadh", now, tenant);
            foreach (var day in new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday })
                calendar.AddPeriod(day, TimeSpan.FromHours(8), TimeSpan.FromHours(16), now);
            db.BusinessCalendars.Add(calendar);
        }
        if (!await db.SlaPolicies.AnyAsync(p => p.PolicyCode == "UNRC-LOCAL"))
        {
            var policy = SlaPolicy.Create("UNRC-LOCAL", "UNRC — 24 hours", 24, SlaDurationUnit.Hours, calendar.Id, now, tenant);
            policy.BindFieldActivity(DepartmentCode, ActivityCode);
            db.SlaPolicies.Add(policy);
        }
        // One existing administrator can exercise all example steps. This does not decide the deferred multi-role model.
        var participant = await db.Participants.SingleOrDefaultAsync(p => p.UserId == administrator);
        if (participant is null)
        {
            participant = WorkflowParticipant.Create(tenant, administrator, "Local administrator", "administrator@localhost", now);
            db.Participants.Add(participant);
        }
        var group = await db.AssignmentGroups.SingleOrDefaultAsync(g => g.Code == "UNRC-LOCAL");
        if (group is null)
        {
            group = WorkflowAssignmentGroup.Create(tenant, "UNRC-LOCAL", "UNRC — Local reviewers", AssignmentStrategy.RoundRobin, now);
            db.AssignmentGroups.Add(group);
        }
        if (!await db.GroupMembers.AnyAsync(m => m.AssignmentGroupId == group.Id && m.ParticipantId == participant.Id))
            db.GroupMembers.Add(WorkflowGroupMember.Create(group.Id, participant.Id, true, true, now));
        await db.SaveChangesAsync();

        async Task<FormDefinition> Form(string code, string name, params (string Key, string Label, string Type, bool Required)[] fields)
        {
            var form = await forms.FormDefinitions.SingleOrDefaultAsync(f => f.Code == code);
            if (form is null)
            {
                form = FormDefinition.Create(code, name, name, "GENERAL", DepartmentCode, ActivityCode, "unrc-local", now);
                form.SetSchema(Json(new { name_en = name, name_ar = name, elements = fields.Select(f => new { type = f.Type, data_name = f.Key, label_en = f.Label, label_ar = f.Label, required = f.Required }) }), name, name, "unrc-local", now);
                forms.FormDefinitions.Add(form);
                await forms.SaveChangesAsync();
            }
            if (form.CurrentVersionNo is null)
            {
                var result = await services.GetRequiredService<IFormPublisher>().PublishAsync(form.Id, "unrc-local", default);
                if (result.IsFailure) throw new InvalidOperationException(result.Error.Message);
                await forms.Entry(form).ReloadAsync();
            }
            return form;
        }
        var registration = await Form("UNRC-REGISTRATION", "UNRC — Registration", ("request_reference", "Request reference", "text", true), ("customer_name", "Customer name", "text", true));
        var location = await Form("UNRC-LOCATION", "UNRC — Go to Location", ("site_address", "Site address", "memo", true));
        var feedback = await Form("UNRC-FEEDBACK", "UNRC — Preliminary feedback", ("release_ready", "Ready to release? Enter yes or no", "text", true), ("feedback", "Preliminary feedback", "memo", true));
        var contractor = await Form("UNRC-CONTRACTOR", "UNRC — Contractor form", ("meter_serial", "Installed meter serial", "text", true), ("contractor_notes", "Contractor notes", "memo", false));
        var consultant = await Form("UNRC-CONSULTANT", "UNRC — Consultant form", ("consultant_notes", "Consultant inspection notes", "memo", true));
        var approval = await Form("UNRC-APPROVAL", "UNRC — Approval review", ("approval_notes", "Review notes", "memo", true));
        var activation = await Form("UNRC-ACTIVATION", "UNRC — Confirm activation", ("activation_reference", "Activation reference", "text", true));
        async Task<TaskType> TaskTypeFor(string code, string name, params Guid[] formIds)
        {
            var type = await tasks.TaskTypes.SingleOrDefaultAsync(t => t.Code == code);
            if (type is not null) return type;
            type = TaskType.Create(code, name, name, "Local UNRC example", null, formIds, null, "unrc-local", now);
            tasks.TaskTypes.Add(type); await tasks.SaveChangesAsync(); return type;
        }
        var meterType = await TaskTypeFor("UNRC-METER", "Meter Installation", registration.Id, location.Id, feedback.Id, contractor.Id, consultant.Id, approval.Id);
        var activationType = await TaskTypeFor("UNRC-ACTIVATION", "Connection Activation", activation.Id);
        var integrations = services.GetRequiredService<IWorkflowIntegrations>();
        var transport = (await integrations.ListConnectionsAsync(default)).SingleOrDefault(c => c.Name == "UNRC — Local simulator");
        if (transport is null)
        {
            var result = await integrations.SaveConnectionAsync(null, new("UNRC — Local simulator", "Http", "http://127.0.0.1:5091", "None", AllowPrivateNetwork: true), default);
            if (result.IsFailure) throw new InvalidOperationException(result.Error.Message);
            transport = result.Value;
        }
        var territory = await (from cbu in auth.Cbus
                               join cluster in auth.Clusters on cbu.ClusterCode equals cluster.Code
                               where cbu.IsActive && cluster.IsActive
                               orderby cluster.Code, cbu.Code
                               select new { Cluster = cluster.Code, Cbu = cbu.Code }).FirstAsync();
        var scopes = new[] { new WorkflowOrganizationScope(OrgLevels.Cbu, territory.Cbu, territory.Cluster, territory.Cbu) };
        var activity = (await services.GetRequiredService<IWorkflowReferenceData>().ListAsync("field-activity-types", null, default)).Single(a => a.Code == ActivityCode);
        var mainSettings = new WorkflowWorkspaceDefinition("Main", DesignerVersion: 3)
        { SchemaVersion = 2, OrganizationScopes = scopes, FieldActivityTypeId = activity.Id, DepartmentCode = activity.ParentCode, FieldActivityCode = ActivityCode };

        async Task<WorkflowDefinition> Definition(string key, string name)
        {
            var definition = await db.WorkflowDefinitions.SingleOrDefaultAsync(d => d.DefinitionKey == key);
            if (definition is not null) return definition;
            definition = WorkflowDefinition.Create(tenant, key, name, now, description: "Local UNRC example. External systems are simulated on loopback.");
            db.WorkflowDefinitions.Add(definition); await db.SaveChangesAsync(); return definition;
        }
        var main = existing ?? await Definition(MainKey, "UNRC — Unregistered Connection");
        var meter = await Definition("UNRC-METER-LOCAL", "UNRC — Meter Installation steps");
        var activationChild = await Definition("UNRC-ACTIVATION-LOCAL", "UNRC — Connection Activation steps");
        WorkflowWorkspaceDefinition ChildSettings(Guid taskTypeId, string node) => new("Child", DesignerVersion: 3)
        { SchemaVersion = 2, OrganizationScopes = scopes, TaskTypeId = taskTypeId, ParentDefinitionId = main.Id, ParentNodeKey = node };
        XElement Step(string key, string name, FormDefinition form, string previous, int order, Guid? taskTypeId = null, object? rules = null, object? otherwise = null, object? mappings = null)
            => Node(key, "UserTask", name, new
            {
                taskTypeId = taskTypeId ?? meterType.Id,
                departmentCode = DepartmentCode,
                fieldActivityCode = ActivityCode,
                rejectTargetNodeKey = previous,
                outputMappingJson = mappings is null ? "{}" : Json(mappings),
                formSteps = new[] { new { id = key + "-form", formId = form.Id, versionNo = form.CurrentVersionNo, name = form.NameEn, rules = rules ?? Array.Empty<object>(), otherwise = otherwise ?? new { kind = "Next" } } }
            }, order, group.Id);
        XElement Event(string key, string name, string path, int order, object mappings, string body, bool required = true, object? binding = null)
            => Node(key, "ServiceTask", name, new
            {
                connectionId = transport.Id,
                protocol = "Rest",
                method = "POST",
                path,
                body,
                executionMode = required ? "Sync" : "Async",
                required,
                maxAttempts = 3,
                retryDelaySeconds = 2,
                timeoutSeconds = 10,
                outputMappings = mappings,
                triggerBinding = binding
            }, order);
        async Task<Guid> Publish(WorkflowDefinition definition, WorkflowWorkspaceDefinition settings, List<XElement> nodes, List<XElement> edges)
        {
            var published = await db.WorkflowVersions.Where(v => v.WorkflowDefinitionId == definition.Id && v.Status == WorkflowVersionStatus.Published).OrderByDescending(v => v.VersionNumber).FirstOrDefaultAsync();
            if (published is not null) return published.Id;
            var draft = await db.WorkflowVersions.Where(v => v.WorkflowDefinitionId == definition.Id && v.Status == WorkflowVersionStatus.Draft).OrderByDescending(v => v.VersionNumber).FirstOrDefaultAsync();
            if (draft is null)
            {
                var created = await sender.Send(new CreateWorkflowDraftCommand(definition.Id, administrator));
                if (created.IsFailure) throw new InvalidOperationException(created.Error.Message);
                draft = (await db.WorkflowVersions.FindAsync(created.Value.Id))!;
            }
            var xml = new XElement(Ns + "Workflow", new XAttribute("workspaceJson", Json(settings)), new XElement(Ns + "Activities", nodes), new XElement(Ns + "Transitions", edges));
            var saved = await sender.Send(new SaveWorkflowDraftXmlCommand(draft.Id, xml.ToString()));
            if (saved.IsFailure) throw new InvalidOperationException(saved.Error.Message);
            var result = await sender.Send(new PublishWorkflowVersionCommand(draft.Id, administrator));
            if (result.IsFailure) throw new InvalidOperationException($"{definition.Name}: {result.Error.Code}: {result.Error.Message}");
            return draft.Id;
        }
        var meterNodes = new List<XElement>
        {
            Node("start", "Start", "Start", null, 0),
            Step("registration", "1.1 Registration", registration, "registration", 1, mappings: new { requestReference = "output.request_reference" }),
            Step("location", "1.2 Go to Location", location, "registration", 2),
            Step("feedback", "1.3 Fill preliminary feedback", feedback, "location", 3,
                rules: new[] { new { id = "release-ready", name = "Ready for contractor release", when = new { match = "All", conditions = new[] { new { field = "release_ready", @operator = "equal", value = "yes" } }, groups = Array.Empty<object>() }, destination = new { kind = "Activity", target = "release" } } },
                otherwise: new { kind = "Activity", target = "feedback" }),
            Event("release", "Release with contractor", "/unrc/release", 4, new { contractorReleaseId = "body.id", releaseAccepted = "body.accepted" }, "{\"reference\":\"{{requestReference}}\"}"),
            Step("contractor", "1.4 Filling Contractor Form", contractor, "feedback", 5, mappings: new { meterSerial = "output.meter_serial" }),
            Step("consultant", "1.5 Filling Consultant Form", consultant, "contractor", 6),
            Step("approve1", "1.6 Approve level 1", approval, "consultant", 7),
            Step("approve2", "1.7 Approve level 2", approval, "approve1", 8),
            Step("approve3", "1.8 Approve level 3", approval, "approve2", 9),
            Event("close", "Close in other system", "/unrc/close", 10, new { externalClosureId = "body.id", closureAccepted = "body.accepted" }, "{\"releaseId\":\"{{contractorReleaseId}}\",\"meterSerial\":\"{{meterSerial}}\"}"),
            Node("end", "End", "Meter installation complete", null, 11),
            Event("registration-notice", "Registration notification", "/unrc/notify", 12, new { }, "{\"reference\":\"{{requestReference}}\"}", false, new { sourceNodeKey = "registration", trigger = "OnComplete" })
        };
        var meterEdges = Chain("start", "registration", "location", "feedback", "release");
        meterEdges.Add(Edge("release", "contractor", "releaseAccepted == true"));
        meterEdges.Add(Edge("release", "feedback", "releaseAccepted != true", 1));
        meterEdges.AddRange(Chain("contractor", "consultant", "approve1", "approve2", "approve3", "close", "end"));
        var meterVersion = await Publish(meter, ChildSettings(meterType.Id, "meter"), meterNodes, meterEdges);
        var activationVersion = await Publish(activationChild, ChildSettings(activationType.Id, "activation"),
            [Node("start", "Start", "Start", null, 0), Step("activation-step", "2.1 Confirm connection activation", activation, "activation-step", 1, activationType.Id), Node("end", "End", "End", null, 2)], Chain("start", "activation-step", "end"));
        XElement TaskNode(string key, string name, Guid typeId, WorkflowDefinition child, Guid version, int order, object mappings) => Node(key, "MainActivity", name, new
        {
            taskTypeId = typeId,
            departmentCode = DepartmentCode,
            fieldActivityCode = ActivityCode,
            definitionKey = child.DefinitionKey,
            versionId = version,
            outputMappings = mappings
        }, order);
        var mainVersion = await Publish(main, mainSettings,
        [
            Node("start", "Start", "Start", null, 0),
            TaskNode("meter", "1 Meter Installation", meterType.Id, meter, meterVersion, 1, new { closureAccepted = "closureAccepted", externalClosureId = "externalClosureId", requestReference = "requestReference" }),
            Event("next-task", "Determine next Task Type", "/unrc/route", 2, new { nextTask = "body.nextTask" }, "{\"closureId\":\"{{externalClosureId}}\",\"reference\":\"{{requestReference}}\"}"),
            TaskNode("activation", "2 Connection Activation", activationType.Id, activationChild, activationVersion, 3, new { }),
            Node("end", "End", "UNRC complete", null, 4)
        ], [Edge("start", "meter"), Edge("meter", "next-task"), Edge("next-task", "activation", "nextTask == 'activation'"), Edge("next-task", "end", "nextTask == 'complete'", 1), Edge("activation", "end")]);
        Console.WriteLine($"UNRC ready: /admin/workflow/definitions/{main.Id}/versions/{mainVersion}/designer");
    }

    private static XElement Node(string key, string type, string name, object? config, int order, Guid? group = null)
    {
        var element = new XElement(Ns + "Activity", new XAttribute("nodeKey", key), new XAttribute("type", type), new XAttribute("name", name),
            new XAttribute("positionX", 240), new XAttribute("positionY", 40 + 150 * order),
            config is null ? null : new XAttribute("configurationJson", Json(config)), type == "ServiceTask" ? new XAttribute("actionKey", "http.request") : null,
            group is null ? null : new XAttribute("assignmentGroupId", group));
        if (config is not null)
        {
            // An absent trigger is a sequence event, not an invalid lifecycle binding.
            var json = System.Text.Json.Nodes.JsonNode.Parse(Json(config))!.AsObject();
            if (json.ContainsKey("triggerBinding") && json["triggerBinding"] is null) json.Remove("triggerBinding");
            element.SetAttributeValue("configurationJson", json.ToJsonString());
        }
        if (group is not null) element.Add(new XElement(Ns + "Outcomes",
            new XElement(Ns + "Outcome", new XAttribute("key", "APPROVE"), new XAttribute("name", "Accept"), new XAttribute("isDefault", true)),
            new XElement(Ns + "Outcome", new XAttribute("key", "REJECT"), new XAttribute("name", "Reject"), new XAttribute("requiresComment", true))));
        return element;
    }
    private static XElement Edge(string from, string to, string? condition = null, int priority = 0) => new(Ns + "Transition",
        new XAttribute("key", from + "-" + to), new XAttribute("from", from), new XAttribute("to", to),
        new XAttribute("priority", priority), condition is null ? null : new XAttribute("condition", condition));
    private static List<XElement> Chain(params string[] keys) => keys.Zip(keys.Skip(1), (from, to) => Edge(from, to)).ToList();
}
