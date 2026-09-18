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
        ManageUsers
    ];
}
