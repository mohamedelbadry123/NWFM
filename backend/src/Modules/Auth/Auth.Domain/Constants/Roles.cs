namespace Auth.Domain.Constants;

public static class Roles
{
    public const string Administrator = nameof(Administrator);
    public const string WorkflowAdmin = nameof(WorkflowAdmin);
    public const string Supervisor = nameof(Supervisor);
    public const string Participant = nameof(Participant);
    public const string FieldTeam = nameof(FieldTeam);
    public const string Monitor = nameof(Monitor);

    public static readonly IReadOnlyList<string> All =
    [
        Administrator,
        WorkflowAdmin,
        Supervisor,
        Participant,
        FieldTeam,
        Monitor
    ];

    public static readonly IReadOnlyDictionary<string, string> DisplayNames = new Dictionary<string, string>
    {
        [Administrator] = "Administrator",
        [WorkflowAdmin] = "Workflow Admin",
        [Supervisor] = "Supervisor",
        [Participant] = "Participant",
        [FieldTeam] = "Field Team",
        [Monitor] = "Monitor"
    };
}
