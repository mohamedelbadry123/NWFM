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

    public const string ViewForms = nameof(ViewForms);
    public const string ManageForms = nameof(ManageForms);
    public const string SubmitForms = nameof(SubmitForms);
    public const string ViewSubmissions = nameof(ViewSubmissions);

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

    /// <summary>Downloading form media: the person filling the form or someone reviewing it.</summary>
    public const string SubmitOrViewSubmissions = AnyPrefix + SubmitForms + "," + ViewSubmissions;

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
        ViewForms,
        ManageForms,
        SubmitForms,
        ViewSubmissions
    ];
}
