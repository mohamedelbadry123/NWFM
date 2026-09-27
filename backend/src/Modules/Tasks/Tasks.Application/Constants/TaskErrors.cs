using NWFM.Shared.Results;

namespace Tasks.Application.Constants;

/// <summary>
/// Every failure the Tasks module reports. The API maps a code to its HTTP status by shape: a code
/// ending in <see cref="NotFoundSuffix"/> is 404, one in <see cref="Conflicts"/> is 409, anything else
/// 400. Failures passed through from the form engine keep the form engine's own codes.
/// </summary>
public static class TaskErrors
{
    public const string NotFoundSuffix = ".NotFound";

    public static class Codes
    {
        public const string TaskNotFound = "Tasks.Task.NotFound";
        public const string TaskDuplicateNumber = "Tasks.Task.DuplicateNumber";
        public const string TaskInvalid = "Tasks.Task.Invalid";
        public const string TaskConcurrencyConflict = "Tasks.Task.ConcurrencyConflict";
        public const string TaskOutsideScope = "Tasks.Task.OutsideScope";
        public const string TypeNotFound = "Tasks.Type.NotFound";
        public const string TypeDuplicateCode = "Tasks.Type.DuplicateCode";
        public const string TypeInactive = "Tasks.Type.Inactive";
        public const string TypeInvalid = "Tasks.Type.Invalid";
        public const string FormNotFound = "Tasks.Form.NotFound";
        public const string FormNotPublished = "Tasks.Form.NotPublished";
        public const string TeamNotFound = "Tasks.Team.NotFound";
        public const string TeamNotEligible = "Tasks.Team.NotEligible";
        public const string C2mRejected = "Tasks.C2m.Rejected";
        public const string C2mUnavailable = "Tasks.C2m.Unavailable";
        public const string C2mNotRetryable = "Tasks.C2m.NotRetryable";
        public const string C2mMappingNotFound = "Tasks.C2mMapping.NotFound";
        public const string C2mMappingDuplicateCode = "Tasks.C2mMapping.DuplicateCode";

        /// <summary>The form engine's own codes that also mean "the state is wrong, not the request".</summary>
        public const string FormEngineNotPublished = "FormEngine.Form.NotPublished";
    }

    /// <summary>Failures caused by the resource's current state rather than by the request — HTTP 409.</summary>
    public static readonly IReadOnlySet<string> Conflicts = new HashSet<string>(StringComparer.Ordinal)
    {
        Codes.TaskDuplicateNumber,
        Codes.TaskInvalid,
        Codes.TaskConcurrencyConflict,
        Codes.TypeDuplicateCode,
        Codes.TypeInactive,
        Codes.FormNotPublished,
        Codes.FormEngineNotPublished,
        Codes.TeamNotEligible,
        Codes.C2mRejected,
        Codes.C2mNotRetryable,
        Codes.C2mMappingDuplicateCode,
    };

    /// <summary>An upstream system did not answer — HTTP 503, worth trying again shortly.</summary>
    public static readonly IReadOnlySet<string> Unavailable = new HashSet<string>(StringComparer.Ordinal)
    {
        Codes.C2mUnavailable,
    };

    /// <summary>Failures the caller is not allowed to act on — HTTP 403.</summary>
    public static readonly IReadOnlySet<string> Forbidden = new HashSet<string>(StringComparer.Ordinal)
    {
        Codes.TaskOutsideScope,
    };

    public static class Task
    {
        /// <summary>Also what a task outside the caller's territory looks like: its existence is not disclosed.</summary>
        public static readonly Error NotFound = new(Codes.TaskNotFound, "Task not found.");

        public static readonly Error ConcurrencyConflict = new(
            Codes.TaskConcurrencyConflict,
            "The task was changed by someone else. Reload it and try again.");

        public static readonly Error OutsideScope = new(
            Codes.TaskOutsideScope,
            "That location is outside your territory.");

        public static Error DuplicateNumber(string number) =>
            new(Codes.TaskDuplicateNumber, $"A task numbered '{number}' already exists.");

        /// <summary>A lifecycle rule refused the change — the message says which.</summary>
        public static Error Invalid(string message) => new(Codes.TaskInvalid, message);
    }

    public static class Type
    {
        public static readonly Error NotFound = new(Codes.TypeNotFound, "Task type not found.");

        public static readonly Error Inactive = new(Codes.TypeInactive, "The task type is inactive; new tasks cannot use it.");

        public static Error DuplicateCode(string code) =>
            new(Codes.TypeDuplicateCode, $"A task type with code '{code}' already exists.");

        public static Error Invalid(string message) => new(Codes.TypeInvalid, message);
    }

    public static class Form
    {
        public static readonly Error NotFound = new(Codes.FormNotFound, "The task type's form could not be found.");

        public static readonly Error NotPublished = new(
            Codes.FormNotPublished,
            "The task type's form has no published version that accepts fills. Publish it first.");
    }

    public static class C2m
    {
        /// <summary>C2M answered and refused. Re-sending the same answers will not change that.</summary>
        public static Error Rejected(string faId, string? message, string? responseCode) =>
            new(
                Codes.C2mRejected,
                $"C2M refused to close field activity {faId}: {message} (code {responseCode ?? "-"}). The task has not been approved.");

        /// <summary>C2M did not answer. Whether it processed the closure is unknown; the attempt is logged.</summary>
        public static Error Unavailable(string faId, string? message) =>
            new(
                Codes.C2mUnavailable,
                $"C2M did not confirm the closure of field activity {faId}: {message} The task has not been approved; try again shortly.");

        public static readonly Error NotRetryable = new(
            Codes.C2mNotRetryable,
            "Only an approved task whose C2M closure was refused or failed can be sent again.");

        public static readonly Error MappingNotFound = new(Codes.C2mMappingNotFound, "C2M action mapping not found.");

        public static Error MappingDuplicateCode(string code) =>
            new(Codes.C2mMappingDuplicateCode, $"A mapping for action code '{code}' already exists.");
    }

    public static class Team
    {
        public static readonly Error NotFound = new(Codes.TeamNotFound, "Team not found.");

        public static readonly Error NotEligible = new(
            Codes.TeamNotEligible,
            "That team is inactive, or its territory does not cover this task.");
    }
}
