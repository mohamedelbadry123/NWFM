namespace Workflow.Application.Constants;

using NWFM.Shared.Results;

public static class WorkflowErrors
{
    public static readonly Error ModuleDisabled =
        new("Workflow.ModuleDisabled", "The Workflow module is not enabled for this environment.");

    public static readonly Error NotFound =
        new("Workflow.NotFound", "The requested workflow resource was not found.");

    public static readonly Error Forbidden =
        new("Workflow.Forbidden", "You do not have permission to perform this action.");

    public static class Participant
    {
        public static readonly Error NotFound =
            new("Workflow.Participant.NotFound", "Workflow participant was not found.");

        public static readonly Error UserNotFound =
            new("Workflow.Participant.UserNotFound", "No active user with this ID exists in the organization.");

        public static readonly Error AlreadyRegistered =
            new("Workflow.Participant.AlreadyRegistered", "This user is already registered as a workflow participant.");
    }

    public static class AssignmentGroup
    {
        public static readonly Error NotFound =
            new("Workflow.AssignmentGroup.NotFound", "Assignment group was not found.");

        public static readonly Error DuplicateCode =
            new("Workflow.AssignmentGroup.DuplicateCode", "An assignment group with this code already exists in the organization.");

        public static readonly Error MemberNotFound =
            new("Workflow.AssignmentGroup.MemberNotFound", "The participant is not a member of this assignment group.");

        public static readonly Error MemberAlreadyExists =
            new("Workflow.AssignmentGroup.MemberAlreadyExists", "This participant is already a member of the assignment group.");
    }

    public static class Department
    {
        public static readonly Error NotFound =
            new("Workflow.Department.NotFound", "Workflow department was not found.");

        public static readonly Error MemberNotFound =
            new("Workflow.Department.MemberNotFound", "The participant is not a member of this department.");

        public static readonly Error MemberAlreadyExists =
            new("Workflow.Department.MemberAlreadyExists", "This participant is already a member of the department.");
    }

    public static class Definition
    {
        public static readonly Error NotFound =
            new("Workflow.Definition.NotFound", "Workflow definition was not found.");

        public static readonly Error DuplicateKey =
            new("Workflow.Definition.DuplicateKey", "A workflow definition with this key already exists in the organization.");

        public static readonly Error CannotDeleteWithVersions =
            new("Workflow.Definition.CannotDeleteWithVersions", "Workflow definitions that have versions cannot be physically deleted.");

        public static readonly Error InactiveCannotPublish =
            new("Workflow.Definition.InactiveCannotPublish", "Inactive definitions cannot receive new published versions.");
    }

    public static class Binding
    {
        public static readonly Error NotFound =
            new("Workflow.Binding.NotFound", "Workflow binding was not found.");

        public static readonly Error DuplicateBinding =
            new("Workflow.Binding.DuplicateBinding", "A binding for this definition, organization, module, entity type, and trigger event already exists.");

        public static readonly Error IncompleteMappings =
            new("Workflow.Binding.IncompleteMappings", "Cannot activate binding: one or more User Tasks do not resolve to an assignment group in this organization.");

        public static readonly Error OrganizationMismatch =
            new("Workflow.Binding.OrganizationMismatch", "A binding can only attach a workflow to a screen in the same organization that owns the workflow.");

        public static readonly Error ShadowOrActiveRequiresMappings =
            new("Workflow.Binding.ShadowOrActiveRequiresMappings", "Shadow and Active mode bindings cannot be activated until all ActivityAssignmentRule keys in the selected version are mapped.");
    }

    public static class AssignmentMapping
    {
        public static readonly Error NotFound =
            new("Workflow.AssignmentMapping.NotFound", "Assignment mapping was not found.");

        public static readonly Error DuplicateKey =
            new("Workflow.AssignmentMapping.DuplicateKey", "A mapping for this AssignmentKey already exists for this binding in this organization.");

        public static readonly Error GroupNotInOrg =
            new("Workflow.AssignmentMapping.GroupNotInOrg", "The specified assignment group does not belong to this organization.");

        public static readonly Error GroupInactive =
            new("Workflow.AssignmentMapping.GroupInactive", "The specified assignment group is inactive.");
    }

    public static class Version
    {
        public static readonly Error NotFound =
            new("Workflow.Version.NotFound", "Workflow version was not found.");

        public static readonly Error NotDraft =
            new("Workflow.Version.NotDraft", "Only Draft versions can be modified.");

        public static readonly Error AlreadyRetired =
            new("Workflow.Version.AlreadyRetired", "This version is already retired.");

        public static readonly Error DraftAlreadyExists =
            new("Workflow.Version.DraftAlreadyExists", "A Draft version already exists for this definition. Complete or retire it before creating a new one.");

        public static readonly Error InvalidXml =
            new("Workflow.Version.InvalidXml", "The workflow XML content is invalid or failed security validation.");

        public static readonly Error ValidationFailed =
            new("Workflow.Version.ValidationFailed", "The workflow version did not pass publish validation. Review the errors and re-validate.");

        public static readonly Error XmlTooLarge =
            new("Workflow.Version.XmlTooLarge", "The workflow XML exceeds the maximum allowed size.");

        public static readonly Error DtdDetected =
            new("Workflow.Version.DtdDetected", "DTD declarations are not permitted in workflow XML.");

        public static readonly Error ExternalEntityDetected =
            new("Workflow.Version.ExternalEntityDetected", "External entity references are not permitted in workflow XML.");

        public static readonly Error ConcurrencyConflict =
            new("Workflow.Version.ConcurrencyConflict", "The version was modified by another process. Please reload and retry.");

        public static readonly Error NotPublished =
            new("Workflow.Version.NotPublished", "Only Published versions can be executed by the runtime engine.");

        public static readonly Error NoPublishedVersion =
            new("Workflow.Version.NoPublishedVersion", "No published version exists for this workflow definition.");
    }

    public static class Instance
    {
        public static readonly Error NotFound =
            new("Workflow.Instance.NotFound", "Workflow instance was not found.");

        public static readonly Error AlreadyStarted =
            new("Workflow.Instance.AlreadyStarted", "A workflow instance for this entity already exists (idempotency key conflict).");

        public static readonly Error NotRunning =
            new("Workflow.Instance.NotRunning", "This operation requires the workflow instance to be in Running status.");

        public static readonly Error NotSuspended =
            new("Workflow.Instance.NotSuspended", "This operation requires the workflow instance to be in Suspended status.");

        public static readonly Error ConcurrencyConflict =
            new("Workflow.Instance.ConcurrencyConflict", "The instance was modified by another process. Please reload and retry.");

        public static readonly Error BindingNotActive =
            new("Workflow.Instance.BindingNotActive", "No active workflow binding was found for this module/entity/trigger combination.");

        public static readonly Error BindingDisabled =
            new("Workflow.Instance.BindingDisabled", "The workflow binding is in Disabled or Paused mode and cannot start instances.");

        public static readonly Error AssignmentKeyNotMapped =
            new("Workflow.Instance.AssignmentKeyNotMapped", "A required AssignmentKey is not mapped to an organization group. Contact your administrator.");

        public static readonly Error UnhandledActivityType =
            new("Workflow.Instance.UnhandledActivityType", "The engine encountered an activity type it cannot yet handle.");

        public static readonly Error NoStartActivity =
            new("Workflow.Instance.NoStartActivity", "The workflow version has no Start activity.");

        public static readonly Error NoOutgoingTransition =
            new("Workflow.Instance.NoOutgoingTransition", "No outgoing transition matched the current instance state.");

        public static readonly Error CallActivityDefinitionMissing =
            new("Workflow.Instance.CallActivityDefinitionMissing", "CallActivity configuration is missing a published definitionKey.");

        public static readonly Error CallActivityNoPublishedVersion =
            new("Workflow.Instance.CallActivityNoPublishedVersion", "CallActivity target definition has no published version.");
    }

    public static class WorkItem
    {
        public static readonly Error NotFound =
            new("Workflow.WorkItem.NotFound", "Work item was not found.");

        public static readonly Error NotPending =
            new("Workflow.WorkItem.NotPending", "Only Pending work items can be claimed.");

        public static readonly Error NotClaimed =
            new("Workflow.WorkItem.NotClaimed", "Only Claimed work items can be released or completed.");

        public static readonly Error NotClaimedByUser =
            new("Workflow.WorkItem.NotClaimedByUser", "You can only release or complete work items that you have claimed.");

        public static readonly Error NotGroupMember =
            new("Workflow.WorkItem.NotGroupMember", "You are not a member of the group this work item is assigned to.");

        public static readonly Error AlreadyCompleted =
            new("Workflow.WorkItem.AlreadyCompleted", "This work item has already been completed.");

        public static readonly Error ConcurrencyConflict =
            new("Workflow.WorkItem.ConcurrencyConflict", "Another user just claimed this work item. Please refresh and try again.");

        public static readonly Error InvalidOutcome =
            new("Workflow.WorkItem.InvalidOutcome", "The selected outcome is not valid for this task.");

        public static readonly Error CommentRequired =
            new("Workflow.WorkItem.CommentRequired", "A comment is required for this outcome.");

        public static readonly Error RedirectTargetRequired =
            new("Workflow.WorkItem.RedirectTargetRequired", "Redirect requires a department or assignment group.");

        public static readonly Error DepartmentHasNoDefaultGroup =
            new("Workflow.WorkItem.DepartmentHasNoDefaultGroup", "The selected department has no default assignment group.");
    }

    public static class Request
    {
        public static readonly Error NotFound =
            new("Workflow.Request.NotFound", "Workflow request was not found.");
    }

    public static class Timer
    {
        public static readonly Error NotFound =
            new("Workflow.Timer.NotFound", "Workflow timer was not found.");

        public static readonly Error NotPending =
            new("Workflow.Timer.NotPending", "Only Pending timers can be fired or cancelled.");

        public static readonly Error NotFired =
            new("Workflow.Timer.NotFired", "Only Fired timers can be completed.");

        public static readonly Error AlreadyTerminal =
            new("Workflow.Timer.AlreadyTerminal", "This timer is already in a terminal state.");

        public static readonly Error SignalKeyRequired =
            new("Workflow.Timer.SignalKeyRequired", "ExternalSignal timers require a SignalKey.");
    }

    public static class Incident
    {
        public static readonly Error NotFound =
            new("Workflow.Incident.NotFound", "Workflow incident was not found.");

        public static readonly Error AlreadyResolved =
            new("Workflow.Incident.AlreadyResolved", "This incident has already been resolved.");

        public static readonly Error AlreadyIgnored =
            new("Workflow.Incident.AlreadyIgnored", "This incident has already been ignored.");

        public static readonly Error NotOpen =
            new("Workflow.Incident.NotOpen", "This operation requires the incident to be Open or InProgress.");
    }

    public static class Calendar
    {
        public static readonly Error NotFound =
            new("Workflow.Calendar.NotFound", "Business calendar was not found.");

        public static readonly Error DuplicateCode =
            new("Workflow.Calendar.DuplicateCode", "An active business calendar with this code already exists.");

        public static readonly Error Inactive =
            new("Workflow.Calendar.Inactive", "The business calendar is inactive.");

        public static readonly Error InvalidTimeZone =
            new("Workflow.Calendar.InvalidTimeZone", "The specified IANA time zone is invalid.");

        public static readonly Error PeriodNotFound =
            new("Workflow.Calendar.PeriodNotFound", "Business calendar period was not found.");

        public static readonly Error HolidayNotFound =
            new("Workflow.Calendar.HolidayNotFound", "Business calendar holiday was not found.");
    }

    public static class Sla
    {
        public static readonly Error NotFound =
            new("Workflow.Sla.NotFound", "SLA policy was not found.");

        public static readonly Error DuplicateCode =
            new("Workflow.Sla.DuplicateCode", "An SLA policy with this policy code already exists.");

        public static readonly Error Inactive =
            new("Workflow.Sla.Inactive", "The SLA policy is inactive.");

        public static readonly Error CalendarRequired =
            new("Workflow.Sla.CalendarRequired", "SLA policies require a valid business calendar.");
    }

    public static class Action
    {
        public static readonly Error NotFound =
            new("Workflow.Action.NotFound", "No workflow action provider is registered for this action key.");

        public static readonly Error ExecutionFailed =
            new("Workflow.Action.ExecutionFailed", "The workflow action failed during execution.");

        public static readonly Error RetryExhausted =
            new("Workflow.Action.RetryExhausted", "The workflow action exhausted its retry budget.");
    }

    public static class Token
    {
        public static readonly Error NotFound =
            new("Workflow.Token.NotFound", "Workflow execution token was not found.");

        public static readonly Error DuplicateBranch =
            new("Workflow.Token.DuplicateBranch", "An execution token for this branch already exists on the workflow instance.");

        public static readonly Error NotActive =
            new("Workflow.Token.NotActive", "Only Active execution tokens can be completed or cancelled.");

        public static readonly Error JoinNotReady =
            new("Workflow.Token.JoinNotReady", "Not all parallel branches have completed; join cannot proceed.");
    }
}
