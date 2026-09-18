namespace Workflow.Application.Constants;
public static class ModuleCatalog
{
 public static IReadOnlyList<ModuleCatalogEntry> GetAll() => [new("Standalone", "Standalone Workflow", "سير عمل مستقل", [new("WorkflowRequest", "Workflow Request", "طلب سير عمل", [new("RequestSubmitted", "Request Submitted", "تم تقديم الطلب", "workflow.start")])])];
}
public sealed record ModuleCatalogEntry(
    string ModuleKey,
    string NameEn,
    string NameAr,
    IReadOnlyList<EntityTypeEntry> EntityTypes);

public sealed record EntityTypeEntry(
    string EntityType,
    string NameEn,
    string NameAr,
    IReadOnlyList<TriggerEventEntry> TriggerEvents);

public sealed record TriggerEventEntry(
    string EventKey,
    string NameEn,
    string NameAr,
    string? ScreenKey = null);
