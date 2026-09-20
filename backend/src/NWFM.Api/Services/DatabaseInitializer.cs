namespace NWFM.Api.Services;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NWFM.Shared.MultiTenancy;
using Workflow.Application.Commands.CreateWorkflowDraft;
using Workflow.Application.Commands.SaveWorkflowDraftXml;
using Workflow.Application.Commands.ValidateWorkflowVersion;
using Workflow.Application.Commands.PublishWorkflowVersion;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<WorkflowDbContext>();
        var options = services.GetRequiredService<IOptions<ApplicationOptions>>().Value;
        await db.Database.MigrateAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;
        if (!await db.Set<Tenant>().AnyAsync(t => t.Id == options.TenantId))
            db.Add(new Tenant { Id = options.TenantId, Name = options.TenantName });
        var actors = new[] { Guid.Parse("20000000-0000-0000-0000-000000000001"), Guid.Parse("20000000-0000-0000-0000-000000000002") };
        for (var i = 0; i < actors.Length; i++)
            if (!await db.Participants.AnyAsync(p => p.UserId == actors[i]))
                db.Participants.Add(WorkflowParticipant.Create(options.TenantId, actors[i], $"Reviewer {i + 1}", $"reviewer{i + 1}@example.test", now, $"مراجع {i + 1}"));
        await db.SaveChangesAsync();
        var group = await db.AssignmentGroups.SingleOrDefaultAsync(g => g.Code == "REVIEWERS");
        if (group is null) {
            group = WorkflowAssignmentGroup.Create(options.TenantId, "REVIEWERS", "Reviewers", AssignmentStrategy.RoundRobin, now, "المراجعون");
            db.AssignmentGroups.Add(group);
            await db.SaveChangesAsync();
        }
        foreach (var p in await db.Participants.Where(p => actors.Contains(p.UserId)).ToListAsync())
            if (!await db.GroupMembers.AnyAsync(m => m.AssignmentGroupId == group.Id && m.ParticipantId == p.Id))
                db.GroupMembers.Add(WorkflowGroupMember.Create(group.Id, p.Id, true, p.UserId == actors[0], now));
        await db.SaveChangesAsync();
        if (!await db.WorkflowDefinitions.AnyAsync(d => d.DefinitionKey == "SIMPLE_APPROVAL")) {
            var definition = WorkflowDefinition.Create(options.TenantId, "SIMPLE_APPROVAL", "Simple approval", now, "موافقة بسيطة");
            db.WorkflowDefinitions.Add(definition);
            await db.SaveChangesAsync();
            var sender = services.GetRequiredService<ISender>();
            var draft = await sender.Send(new CreateWorkflowDraftCommand(definition.Id, actors[0]));
            if (draft.IsFailure) throw new InvalidOperationException(draft.Error.Message);
            var xml = $$"""
            <Workflow xmlns="https://privora.io/workflow/v1">
              <Activities>
                <Activity nodeKey="start" type="Start" name="Start" positionX="80" positionY="160" />
                <Activity nodeKey="review" type="UserTask" name="Review request" positionX="260" positionY="140" assignmentGroupId="{{group.Id}}" />
                <Activity nodeKey="end" type="End" name="Completed" positionX="620" positionY="160" />
              </Activities>
              <Transitions>
                <Transition key="t1" from="start" to="review" />
                <Transition key="t2" from="review" to="end" />
              </Transitions>
            </Workflow>
            """;
            var saved = await sender.Send(new SaveWorkflowDraftXmlCommand(draft.Value.Id, xml));
            if (saved.IsFailure) throw new InvalidOperationException(saved.Error.Message);
            var validation = await sender.Send(new ValidateWorkflowVersionCommand(draft.Value.Id));
            if (validation.IsFailure) throw new InvalidOperationException(validation.Error.Message);
            var published = await sender.Send(new PublishWorkflowVersionCommand(draft.Value.Id, actors[0]));
            if (published.IsFailure) throw new InvalidOperationException(published.Error.Message);
            var binding = WorkflowBinding.Create(definition.Id, options.TenantId, "Standalone", "WorkflowRequest", "RequestSubmitted", now, mode: WorkflowBindingMode.Active, screenKey: "workflow.start");
            binding.Activate(now);
            db.WorkflowBindings.Add(binding);
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
    }
}
