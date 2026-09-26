namespace NWFM.Shared.Constants;

public static class NwfmPolicies
{
    public const string CanManageRolePermissions = nameof(CanManageRolePermissions);
    public const string CanPurge = nameof(CanPurge);

    public const string ViewWorkflows = nameof(ViewWorkflows);
    public const string StartWorkflows = nameof(StartWorkflows);
    public const string ClaimTasks = nameof(ClaimTasks);
    public const string ManageDefinitions = nameof(ManageDefinitions);
    public const string ManageBindings = nameof(ManageBindings);
    public const string ManageCalendars = nameof(ManageCalendars);
    public const string ManageSlaPolicies = nameof(ManageSlaPolicies);
    public const string ViewInstances = nameof(ViewInstances);
    public const string ManageIncidents = nameof(ManageIncidents);
    public const string ManageDeadLetters = nameof(ManageDeadLetters);
    public const string ManageParticipants = nameof(ManageParticipants);
    public const string ManageGroups = nameof(ManageGroups);
    public const string ViewWorkload = nameof(ViewWorkload);
    public const string ManageLookups = nameof(ManageLookups);
    public const string ManageUsers = nameof(ManageUsers);
    public const string ManageTeams = nameof(ManageTeams);

    public const string ViewForms = nameof(ViewForms);
    public const string ManageForms = nameof(ManageForms);
    public const string SubmitForms = nameof(SubmitForms);
    public const string ViewSubmissions = nameof(ViewSubmissions);

    /// <summary>
    /// Field tasks. <c>ClaimTasks</c> above is the workflow engine's own permission for work items —
    /// a different thing — which is why these all carry a <c>Field</c>-free, <c>Tasks</c>-suffixed name.
    /// </summary>
    public const string ViewTasks = nameof(ViewTasks);
    public const string ManageTasks = nameof(ManageTasks);
    public const string AssignTasks = nameof(AssignTasks);
    public const string SubmitTasks = nameof(SubmitTasks);
    public const string ReviewTasks = nameof(ReviewTasks);
    public const string ManageTaskTypes = nameof(ManageTaskTypes);

    /// <summary>
    /// Prefix of a composite policy satisfied by holding any one of the listed permissions,
    /// e.g. <c>Permissions.Any:SubmitForms,ViewSubmissions</c>. Understood by both the ASP.NET policy
    /// provider and the MediatR <c>AuthorizationBehavior</c>. Composites are never seeded, so they
    /// stay out of <see cref="All"/>.
    /// </summary>
    public const string AnyPrefix = "Permissions.Any:";

    /// <summary>Reading a published form schema: whoever designs, fills or reviews forms.</summary>
    public const string FormSchemaReaders = AnyPrefix + ViewForms + "," + SubmitForms + "," + ViewSubmissions;

    /// <summary>Listing the forms that can be filled.</summary>
    public const string FormPickers = AnyPrefix + ViewForms + "," + SubmitForms;

    /// <summary>Uploading form media: filling a form directly, or filling one as part of a task.</summary>
    public const string FormUploaders = AnyPrefix + SubmitForms + "," + SubmitTasks;

    /// <summary>
    /// Downloading form media: the person filling the form, someone reviewing its submissions, or
    /// someone working on or viewing the task the fill belongs to.
    /// </summary>
    public const string SubmitOrViewSubmissions =
        AnyPrefix + SubmitForms + "," + ViewSubmissions + "," + SubmitTasks + "," + ViewTasks;

    /// <summary>Listing teams: to manage them, or to put a user on one.</summary>
    public const string TeamReaders = AnyPrefix + ManageTeams + "," + ManageUsers;

    /// <summary>Reading task types: to configure them, or to pick one when working with tasks.</summary>
    public const string TaskTypeReaders = AnyPrefix + ViewTasks + "," + ManageTaskTypes;

    public static readonly IReadOnlyList<string> All =
    [
        CanManageRolePermissions,
        CanPurge,
        ViewWorkflows,
        StartWorkflows,
        ClaimTasks,
        ManageDefinitions,
        ManageBindings,
        ManageCalendars,
        ManageSlaPolicies,
        ViewInstances,
        ManageIncidents,
        ManageDeadLetters,
        ManageParticipants,
        ManageGroups,
        ViewWorkload,
        ManageLookups,
        ManageUsers,
        ManageTeams,
        ViewForms,
        ManageForms,
        SubmitForms,
        ViewSubmissions,
        ViewTasks,
        ManageTasks,
        AssignTasks,
        SubmitTasks,
        ReviewTasks,
        ManageTaskTypes
    ];
}
