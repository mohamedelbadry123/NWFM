using FormEngine.Application.Common.Schema;
using NWFM.Shared.Results;

namespace FormEngine.Application.Constants;

/// <summary>
/// Every failure the FormEngine module reports. The API maps a code to its HTTP status by shape:
/// a code ending in <see cref="NotFoundSuffix"/> is 404, one listed in <see cref="Conflicts"/> is 409,
/// and anything else is 400.
/// </summary>
public static class FormEngineErrors
{
    public const string NotFoundSuffix = ".NotFound";

    public static class Codes
    {
        public const string FormNotFound = "FormEngine.Form.NotFound";
        public const string FormDuplicateCode = "FormEngine.Form.DuplicateCode";
        public const string FormNotEditable = "FormEngine.Form.NotEditable";
        public const string FormInvalidStatusTransition = "FormEngine.Form.InvalidStatusTransition";
        public const string FormNotPublished = "FormEngine.Form.NotPublished";
        public const string FormConcurrencyConflict = "FormEngine.Form.ConcurrencyConflict";
        public const string FormInvalid = "FormEngine.Form.Invalid";
        public const string VersionNotFound = "FormEngine.Version.NotFound";
        public const string SchemaEmpty = "FormEngine.Schema.Empty";
        public const string SchemaInvalidJson = "FormEngine.Schema.InvalidJson";
        public const string SchemaInvalidDataName = "FormEngine.Schema.InvalidDataName";
        public const string SchemaDuplicateDataName = "FormEngine.Schema.DuplicateDataName";
        public const string SchemaReservedDataName = "FormEngine.Schema.ReservedDataName";
        public const string SchemaTooManyFields = "FormEngine.Schema.TooManyFields";
        public const string FieldTypeConflict = "FormEngine.Field.TypeConflict";
        public const string SubmissionNotFound = "FormEngine.Submission.NotFound";
        public const string SubmissionAnswersInvalid = "FormEngine.Submission.AnswersInvalid";
        public const string SubmissionAnswerRejected = "FormEngine.Submission.AnswerRejected";
        public const string FileNotFound = "FormEngine.File.NotFound";
        public const string FileEmpty = "FormEngine.File.Empty";
        public const string FileTooLarge = "FormEngine.File.TooLarge";
        public const string FileContentTypeNotAllowed = "FormEngine.File.ContentTypeNotAllowed";
        public const string FileExtensionNotAllowed = "FormEngine.File.ExtensionNotAllowed";
        public const string FileNotDeletable = "FormEngine.File.NotDeletable";
        public const string FileForbidden = "FormEngine.File.Forbidden";
    }

    /// <summary>Failures caused by the resource's current state rather than by the request — HTTP 409.</summary>
    public static readonly IReadOnlySet<string> Conflicts = new HashSet<string>(StringComparer.Ordinal)
    {
        Codes.FormDuplicateCode,
        Codes.FormNotEditable,
        Codes.FormInvalidStatusTransition,
        Codes.FormNotPublished,
        Codes.FormConcurrencyConflict,
        Codes.FieldTypeConflict,
        Codes.FileNotDeletable,
    };

    /// <summary>Failures the caller is not allowed to see or change — HTTP 403.</summary>
    public static readonly IReadOnlySet<string> Forbidden = new HashSet<string>(StringComparer.Ordinal)
    {
        Codes.FileForbidden,
    };

    public static class Form
    {
        public static readonly Error NotFound = new(Codes.FormNotFound, "Form not found.");

        public static readonly Error NotPublished = new(
            Codes.FormNotPublished,
            "The form has no published version that accepts submissions.");

        public static readonly Error ConcurrencyConflict = new(
            Codes.FormConcurrencyConflict,
            "The form was changed by someone else. Reload it and try again.");

        public static Error DuplicateCode(string code) =>
            new(Codes.FormDuplicateCode, $"A form with code '{code}' already exists.");

        public static Error NotEditable(string status) =>
            new(Codes.FormNotEditable, $"A {status} form cannot be edited.");

        public static Error InvalidStatusTransition(string status, string action) =>
            new(Codes.FormInvalidStatusTransition, $"A {status} form cannot be {action}.");

        public static Error Invalid(string message) => new(Codes.FormInvalid, message);
    }

    public static class Version
    {
        public static readonly Error NotFound = new(Codes.VersionNotFound, "Form version not found.");
    }

    public static class Schema
    {
        public static readonly Error Empty = new(
            Codes.SchemaEmpty,
            "A form must contain at least one field before it can be published.");

        public static readonly Error InvalidJson = new(Codes.SchemaInvalidJson, "The form schema is not valid JSON.");

        public static Error InvalidDataName(IEnumerable<string> names) =>
            new(Codes.SchemaInvalidDataName,
                $"Field '{string.Join("', '", names)}' cannot be published: {FormDataName.RuleDescription}.");

        public static Error DuplicateDataName(IEnumerable<string> names) =>
            new(Codes.SchemaDuplicateDataName,
                $"Field data name '{string.Join("', '", names)}' is used more than once. Every field needs its own data name.");

        public static Error ReservedDataName(IEnumerable<string> names) =>
            new(Codes.SchemaReservedDataName,
                $"Field data name '{string.Join("', '", names)}' is reserved for submission metadata. Rename the field.");

        public static Error TooManyFields(int count, int max) =>
            new(Codes.SchemaTooManyFields,
                $"The form has {count} stored fields; a form can hold at most {max}. Split it into more than one form.");
    }

    public static class Field
    {
        public static Error TypeConflict(string dataName, string existingType, string requestedType) =>
            new(Codes.FieldTypeConflict,
                $"Field '{dataName}' was published in this form as type '{existingType}' and cannot become '{requestedType}': " +
                "its column already holds answers of the first type. Give the field a new data name instead.");
    }

    public static class Submission
    {
        public static readonly Error NotFound = new(Codes.SubmissionNotFound, "Submission not found.");

        public static Error AnswersInvalid(IEnumerable<FormAnswerError> errors) =>
            new(Codes.SubmissionAnswersInvalid, string.Join(" ", errors.Select(e => e.Message)));

        public static Error AnswerRejected(string message) => new(Codes.SubmissionAnswerRejected, message);
    }

    public static class File
    {
        public static readonly Error NotFound = new(Codes.FileNotFound, "File not found.");

        public static readonly Error Missing = new(Codes.FileNotFound, "The stored file is no longer available.");

        public static readonly Error Empty = new(Codes.FileEmpty, "An empty file cannot be uploaded.");

        public static readonly Error NotDeletable = new(
            Codes.FileNotDeletable,
            "The file belongs to a submitted form and cannot be removed.");

        public static readonly Error Forbidden = new(Codes.FileForbidden, "You do not have access to this file.");

        public static Error TooLarge(int maxMb) =>
            new(Codes.FileTooLarge, $"The file exceeds the {maxMb} MB upload limit.");

        public static Error ContentTypeNotAllowed(string contentType) =>
            new(Codes.FileContentTypeNotAllowed, $"Content type '{contentType}' is not accepted.");

        public static Error ExtensionNotAllowed(string fileName, string dataName, IEnumerable<string> allowed) =>
            new(Codes.FileExtensionNotAllowed,
                $"'{fileName}' is not an accepted file for '{dataName}'. Allowed: {string.Join(", ", allowed)}.");
    }
}
