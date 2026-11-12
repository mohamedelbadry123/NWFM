using FormEngine.Application.Common;
using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Common.Schema;
using FormEngine.Application.Constants;
using FormEngine.Application.Submissions.Models;
using FormEngine.Domain.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Options;
using NWFM.Shared.Results;
using NWFM.Shared.Storage;

namespace FormEngine.Application.Submissions.Common;

/// <inheritdoc />
public sealed class FormSubmissionService(
    IFormEngineDbContext context,
    IFormSubmissionStore submissionStore,
    IFormPublisher publisher,
    IFileStorage fileStorage,
    ICurrentUser user,
    TimeProvider timeProvider,
    IOptions<FileStorageOptions> fileStorageOptions,
    IOptions<FormEngineOptions> formEngineOptions,
    IEnumerable<NWFM.Shared.Integration.Forms.IFormSubmissionGuard>? guards = null,
    NWFM.Shared.Integration.Forms.TrustedFormSubmissionActor? trustedActor = null) : IFormSubmissionService
{
    public async Task<Result<FormSubmissionCreatedDto>> SubmitAsync(FormSubmissionRequest request, CancellationToken ct)
    {
        var attribution = trustedActor?.Resolve(request.ContextType, request.ContextId, user.Id);
        var submitter = attribution?.EffectiveActorId.ToString() ?? user.Id;
        foreach (var guard in guards ?? [])
        {
            var permitted = await guard.ValidateAsync(request.FormDefinitionId, request.VersionNo, request.ContextType,
                request.ContextId, request.ClientSubmissionId, request.Answers, submitter, ct);
            if (permitted.IsFailure) return Result.Failure<FormSubmissionCreatedDto>(permitted.Error);
        }
        var workflowReceipt = string.Equals(request.ContextType, "WorkItem", StringComparison.OrdinalIgnoreCase);
        if (!workflowReceipt && request.ClientSubmissionId is Guid reservedKey
            && await context.WorkflowReceiptKeys.AnyAsync(k => k.ClientSubmissionId == reservedKey, ct))
            return Result.Failure<FormSubmissionCreatedDto>(new Error("Workflow.Form.Context", "This receipt key is reserved for a workflow submission."));
        var resolved = await FormSchemaLoader.LoadAsync(context, request.FormDefinitionId, request.VersionNo, ct);

        if (resolved is null)
        {
            return Result.Failure<FormSubmissionCreatedDto>(
                request.VersionNo is null ? FormEngineErrors.Form.NotFound : FormEngineErrors.Version.NotFound);
        }

        var form = resolved.Form;

        // A form reopened as a draft still accepts fills against the version it published; only
        // deprecating or archiving it, or never having published at all, closes it.
        if (!form.AcceptsSubmissions || resolved.VersionNo is not int versionNo)
        {
            return Result.Failure<FormSubmissionCreatedDto>(FormEngineErrors.Form.NotPublished);
        }

        if (resolved.Schema.Fields.Count == 0)
        {
            return Result.Failure<FormSubmissionCreatedDto>(FormEngineErrors.Schema.Empty);
        }

        var tableResult = await LoadWritableTableAsync(form.Id, resolved.Schema, ct);
        if (tableResult.IsFailure)
        {
            return Result.Failure<FormSubmissionCreatedDto>(tableResult.Error);
        }

        var table = tableResult.Value;

        // A replay of a fill already accepted. Answering with the original id — rather than a
        // conflict — is what lets a client treat a lost response and a successful one identically.
        if (request.ClientSubmissionId is Guid clientKey)
        {
            var recorded = await submissionStore.FindByClientIdAsync(table, clientKey, ct);
            if (recorded is Guid existingId)
            {
                return await ReplayAsync(table, existingId, versionNo, request, submitter, ct);
            }
        }

        // Re-keyed onto the trimmed names the schema is read under, before anything matches answers
        // against it — see FormAnswerKeys.
        var answers = FormAnswerKeys.Normalize(request.Answers);

        var answerErrors = FormAnswerValidator.Validate(
            resolved.Schema,
            answers,
            ResolveFillClock(request.ClientFilledAt),
            formEngineOptions.Value.EnforceFieldRules);

        if (answerErrors.Count > 0)
        {
            return Result.Failure<FormSubmissionCreatedDto>(FormEngineErrors.Submission.AnswersInvalid(answerErrors));
        }

        var utcNow = timeProvider.GetUtcNow();

        await using var transaction = await context.BeginTransactionIfNoneAsync(ct);

        Guid submissionId;

        try
        {
            await SignatureDataUrlNormalizer.NormalizeAsync(
                context,
                fileStorage,
                fileStorageOptions.Value,
                resolved.Schema,
                form.Id,
                versionNo,
                request.ContextType,
                request.ContextId,
                user.Id,
                answers,
                ct);

            submissionId = await submissionStore.InsertAsync(
                table,
                new FormSubmissionInsert
                {
                    VersionNo = versionNo,
                    ContextType = request.ContextType,
                    ContextId = request.ContextId,
                    SubmittedBy = submitter,
                    SubmittedByName = attribution?.DisplayName ?? user.UserName,
                    ClientSubmissionId = request.ClientSubmissionId,
                    Answers = answers,
                },
                ct);

            await SubmissionMediaLinker.LinkAsync(
                context,
                fileStorage,
                resolved.Schema,
                answers,
                form.Id,
                submissionId,
                DestinationFolder(form.Code, submissionId),
                request.ContextType,
                request.ContextId,
                utcNow.UtcDateTime,
                ct);

            if (workflowReceipt && request.ClientSubmissionId is Guid acceptedKey
                && !await context.WorkflowReceiptKeys.AnyAsync(k => k.ClientSubmissionId == acceptedKey, ct))
                context.WorkflowReceiptKeys.Add(new FormEngine.Domain.Entities.WorkflowReceiptKey(acceptedKey));
            await context.SaveChangesAsync(ct);

            if (transaction is not null)
            {
                await transaction.CommitAsync(ct);
            }
        }
        catch (DuplicateClientSubmissionException duplicate)
        {
            // Two retries raced each other; the one that lost reports the row the winner wrote.
            var recorded = await submissionStore.FindByClientIdAsync(table, duplicate.ClientSubmissionId, ct);

            return recorded is Guid existingId
                ? await ReplayAsync(table, existingId, versionNo, request, submitter, ct)
                : Result.Failure<FormSubmissionCreatedDto>(
                    FormEngineErrors.Submission.AnswerRejected(duplicate.Message));
        }
        catch (DomainException ex)
        {
            // An answer that cannot be converted to its column's type — the message names the field.
            return Result.Failure<FormSubmissionCreatedDto>(FormEngineErrors.Submission.AnswerRejected(ex.Message));
        }

        return Result.Success(new FormSubmissionCreatedDto(submissionId, versionNo, IsReplay: false));
    }

    private async Task<Result<FormSubmissionCreatedDto>> ReplayAsync(FormTable table, Guid id, int version,
        FormSubmissionRequest request, string? submitter, CancellationToken ct)
    {
        // The owning workflow transaction can roll back after this immutable receipt committed.
        // Its stored context remains authoritative even if the workflow reservation rolled back.
        var row = await submissionStore.GetByIdAsync(table, id, ct);
        var storedContext = row?.GetValueOrDefault("ContextType") as string;
        if (string.Equals(storedContext, "WorkItem", StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.ContextType, "WorkItem", StringComparison.OrdinalIgnoreCase))
        {
            var owner = row?.GetValueOrDefault("SubmittedBy") as string;
            if (row is null || !string.Equals(storedContext, request.ContextType, StringComparison.OrdinalIgnoreCase)
                || !Equals(row.GetValueOrDefault("ContextId"), request.ContextId)
                || !Equals(row.GetValueOrDefault("VersionNo"), version)
                || (!string.IsNullOrWhiteSpace(owner) && (!Guid.TryParse(owner, out var recordedActor)
                    || !Guid.TryParse(submitter, out var currentActor) || recordedActor != currentActor)))
                return Result.Failure<FormSubmissionCreatedDto>(new Error("Workflow.Form.Context", "The completion receipt does not belong to this workflow submission."));
        }
        return Result.Success(new FormSubmissionCreatedDto(id, version, IsReplay: true));
    }

    /// <summary>
    /// The form's table, narrowed to the columns the answered version declares. The table holds every
    /// field any version introduced, but a fill of version 1 must not be able to write a field only
    /// version 3 has — that answer was never asked.
    ///
    /// A version whose columns are missing — a form published before it had a table of its own, or a
    /// database restored without the native tables — is repaired here rather than failing the fill.
    /// </summary>
    private async Task<Result<FormTable>> LoadWritableTableAsync(Guid formDefinitionId, FormSchema schema, CancellationToken ct)
    {
        var declared = FormWritableFields.Of(schema).Select(f => f.Name).ToList();

        var table = await FormTableLoader.LoadAsync(context, formDefinitionId, ct);

        if (table is null || declared.Any(name => !table.FieldTypes.ContainsKey(name)))
        {
            var repaired = await publisher.EnsureStorageAsync(formDefinitionId, ct);
            if (repaired.IsFailure)
            {
                return Result.Failure<FormTable>(repaired.Error);
            }

            table = await FormTableLoader.LoadAsync(context, formDefinitionId, ct);
            if (table is null)
            {
                return Result.Failure<FormTable>(FormEngineErrors.Form.NotPublished);
            }
        }

        return Result.Success(FormTable.Create(
            table.FormDefinitionId,
            table.TableName,
            table.FieldTypes.Where(field => declared.Contains(field.Key, StringComparer.OrdinalIgnoreCase))));
    }

    /// <summary>Where a submission's media is filed once it is claimed.</summary>
    private string DestinationFolder(string formCode, Guid submissionId) =>
        $"{fileStorageOptions.Value.SubmissionsFolder}/{formCode}/{submissionId:N}";

    /// <summary>
    /// The local wall clock a date rule is judged against.
    ///
    /// Local, not UTC: an answer is a calendar value read off a device set to local time, and a few
    /// hours of offset move "today" for every fill late in the evening. A client-supplied fill time is
    /// honoured so an offline sync is judged on the day the work was done, but never later than the
    /// server's own clock.
    /// </summary>
    private DateTime ResolveFillClock(DateTimeOffset? clientFilledAt)
    {
        var zone = formEngineOptions.Value.ResolveTimeZone();
        var serverNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime;

        if (clientFilledAt is not { } filledAt)
        {
            return serverNow;
        }

        var clientNow = TimeZoneInfo.ConvertTime(filledAt, zone).DateTime;
        return clientNow < serverNow ? clientNow : serverNow;
    }
}
