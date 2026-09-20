using FormEngine.Application.Common;
using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Common.Schema;
using FormEngine.Application.Constants;
using FormEngine.Application.Submissions.Common;
using FormEngine.Application.Submissions.Models;
using FormEngine.Domain.Options;
using MediatR;
using Microsoft.Extensions.Options;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Options;
using NWFM.Shared.Results;
using NWFM.Shared.Storage;

namespace FormEngine.Application.Submissions.Commands.SubmitForm;

public sealed class SubmitFormCommandHandler(
    IFormEngineDbContext context,
    IFormSubmissionStore submissionStore,
    IFileStorage fileStorage,
    ICurrentUser user,
    TimeProvider timeProvider,
    IOptions<FileStorageOptions> fileStorageOptions,
    IOptions<FormEngineOptions> formEngineOptions)
    : IRequestHandler<SubmitFormCommand, Result<FormSubmissionCreatedDto>>
{
    public async Task<Result<FormSubmissionCreatedDto>> Handle(SubmitFormCommand request, CancellationToken ct)
    {
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

        // A replay of a fill already accepted. Answering with the original id — rather than a
        // conflict — is what lets a client treat a lost response and a successful one identically.
        if (request.ClientSubmissionId is Guid clientKey)
        {
            var recorded = await submissionStore.FindByClientIdAsync(form.Id, clientKey, ct);
            if (recorded is Guid existingId)
            {
                return Result.Success(new FormSubmissionCreatedDto(existingId, versionNo, IsReplay: true));
            }
        }

        // Safety net: publishing already added these columns, but a form published by an older build
        // (or a table restored from elsewhere) must not fail the fill.
        await submissionStore.ReconcileTableAsync(resolved.Schema, ct);

        // Re-keyed onto the trimmed names the schema is read under, before anything matches answers
        // against it — see FormAnswerKeys.
        var answers = FormAnswerKeys.Normalize(request.Answers);

        var answerErrors = FormAnswerValidator.Validate(
            resolved.Schema,
            answers,
            ResolveFillClock(request),
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
                new FormSubmissionInsert
                {
                    FormDefinitionId = form.Id,
                    VersionNo = versionNo,
                    Schema = resolved.Schema,
                    ContextType = request.ContextType,
                    ContextId = request.ContextId,
                    SubmittedBy = user.Id,
                    SubmittedByName = user.UserName,
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

            await context.SaveChangesAsync(ct);

            if (transaction is not null)
            {
                await transaction.CommitAsync(ct);
            }
        }
        catch (DuplicateClientSubmissionException duplicate)
        {
            // Two retries raced each other; the one that lost reports the row the winner wrote.
            var recorded = await submissionStore.FindByClientIdAsync(form.Id, duplicate.ClientSubmissionId, ct);

            return recorded is Guid existingId
                ? Result.Success(new FormSubmissionCreatedDto(existingId, versionNo, IsReplay: true))
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
    private DateTime ResolveFillClock(SubmitFormCommand request)
    {
        var zone = formEngineOptions.Value.ResolveTimeZone();
        var serverNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime;

        if (request.ClientFilledAt is not { } filledAt)
        {
            return serverNow;
        }

        var clientNow = TimeZoneInfo.ConvertTime(filledAt, zone).DateTime;
        return clientNow < serverNow ? clientNow : serverNow;
    }
}
