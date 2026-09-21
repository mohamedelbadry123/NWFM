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

    public static class Team
    {
        public static readonly Error NotFound = new(Codes.TeamNotFound, "Team not found.");

        public static readonly Error NotEligible = new(
            Codes.TeamNotEligible,
            "That team is inactive, or its territory does not cover this task.");
    }
}
