using FormEngine.Application.Common.Interfaces;
using FormEngine.Domain.Entities;
using FormEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Tasks.Domain.Entities;
using Tasks.Infrastructure.Persistence;

namespace NWFM.Api.Services;

/// <summary>Optional notes keep approval demos usable while exercising real published form bindings.</summary>
internal static class WorkflowDemoForms
{
    internal static async Task<(Guid TaskTypeId, Guid FormId, int VersionNo)> EnsureAsync(IServiceProvider services)
    {
        var forms = services.GetRequiredService<FormEngineDbContext>();
        var form = await forms.FormDefinitions.SingleOrDefaultAsync(f => f.Code == "DEMO-WORKFLOW-NOTES");
        if (form is null)
        {
            form = FormDefinition.Create("DEMO-WORKFLOW-NOTES", "Workflow approval notes", "ملاحظات اعتماد سير العمل", "GENERAL", null, null, "system", DateTime.UtcNow);
            form.SetSchema("""{"name_en":"Workflow approval notes","name_ar":"ملاحظات اعتماد سير العمل","elements":[{"type":"memo","data_name":"approval_notes","label_en":"Approval notes","label_ar":"ملاحظات الاعتماد","required":false}]}""", form.NameEn, form.NameAr, "system", DateTime.UtcNow);
            forms.FormDefinitions.Add(form);
            await forms.SaveChangesAsync();
        }
        if (form.CurrentVersionNo is null)
        {
            var published = await services.GetRequiredService<IFormPublisher>().PublishAsync(form.Id, "system", default);
            if (published.IsFailure) throw new InvalidOperationException(published.Error.Message);
            await forms.Entry(form).ReloadAsync();
        }
        var tasks = services.GetRequiredService<TasksDbContext>();
        var type = await tasks.TaskTypes.SingleOrDefaultAsync(t => t.Code == "DEMO_WORKFLOW_APPROVAL");
        if (type is null)
        {
            type = TaskType.Create("DEMO_WORKFLOW_APPROVAL", "Workflow approval", "اعتماد سير العمل", null, null, [form.Id], null, "system", DateTime.UtcNow);
            tasks.TaskTypes.Add(type);
            await tasks.SaveChangesAsync();
        }
        return (type.Id, form.Id, form.CurrentVersionNo!.Value);
    }
}
