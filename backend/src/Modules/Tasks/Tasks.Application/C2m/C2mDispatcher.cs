using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWFM.Shared.Integration.Forms;
using Tasks.Application.Common.Interfaces;
using Tasks.Domain.Constants;
using Tasks.Domain.Entities;

namespace Tasks.Application.C2m;

/// <summary>Closes a task's field activity in C2M from its latest fill, and records the attempt.</summary>
public interface IC2mDispatcher
{
    /// <param name="timeout">Caps the wait when a reviewer is waiting on the answer; null for the background path.</param>
    Task<C2mDispatchOutcome> DispatchAsync(
        FieldTask task,
        DateTime completionTime,
        TimeSpan? timeout,
        CancellationToken cancellationToken);
}

/// <summary>
/// Builds and sends the closure. Ported from the reference app's dispatcher.
/// </summary>
/// <remarks>
/// Every outcome — accepted, refused, unreachable, integration off — becomes a
/// <see cref="C2mDispatchLog"/> row, the only durable record that a closure was attempted.
/// <list type="bullet">
/// <item>The outcome comes from the fill: the Action Taken answer through
/// <see cref="IC2mActionMappingResolver"/>.</item>
/// <item><c>ParametersList</c> is built from the answered version: each field with a
/// <c>c2m_parameter_name</c> is sent when answered. Blank answers are left out, never defaulted.</item>
/// <item>The pending row is saved before the call, so a crash mid-request still leaves evidence that
/// C2M may have been told. Nothing here changes the task; the final log state is left unsaved for
/// the caller to commit with its own changes.</item>
/// </list>
/// </remarks>
public sealed class C2mDispatcher(
    ITasksDbContext db,
    IFormGateway forms,
    IC2mClient client,
    IC2mActionMappingResolver resolver,
    IOptions<C2mOptions> options,
    TimeProvider clock,
    ILogger<C2mDispatcher> logger)
    : IC2mDispatcher
{
    /// <summary>The format C2M's samples use for <c>completionDTTM</c>.</summary>
    private const string CompletionDateFormat = "yyyy-MM-dd-HH.mm.ss";

    private const string FaIdPlaceholder = "{faId}";

    /// <summary>Sent as <c>comment</c> when the crew wrote no remarks, as C2M's samples do.</summary>
    private const string EmptyComment = "-";

    private static readonly JsonSerializerOptions LogJson = new() { WriteIndented = false };

    public async Task<C2mDispatchOutcome> DispatchAsync(
        FieldTask task,
        DateTime completionTime,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(task.FaId))
        {
            return C2mDispatchOutcome.Skipped("The task carries no FA id.");
        }

        // Already settled: re-sending would ask C2M to settle a finished activity twice, the one harm a
        // retry can actually do.
        var settled = await db.C2mDispatchLogs
            .AsNoTracking()
            .Where(x => x.TaskId == task.Id && x.Status == C2mDispatchStatuses.Succeeded)
            .OrderByDescending(x => x.AttemptNumber)
            .Select(x => new { x.ResponseCode })
            .FirstOrDefaultAsync(cancellationToken);

        if (settled is not null)
        {
            return C2mDispatchOutcome.AlreadyAcknowledged(settled.ResponseCode);
        }

        var contextId = task.Id.ToString("D");
        var fill = await forms.GetLatestByContextAsync(task.FormDefinitionId, TasksSchema.FormContextType, contextId, cancellationToken);
        var answers = fill?.Answers ?? new Dictionary<string, object?>();
        var fields = await forms.GetFieldsAsync(task.FormDefinitionId, fill?.VersionNo ?? task.FormVersionNo, cancellationToken);
        var byName = new Dictionary<string, object?>(answers, StringComparer.OrdinalIgnoreCase);

        var actionCode = AsText(byName.GetValueOrDefault(C2mFieldNames.ActionTaken));
        var outcome = await resolver.ResolveAsync(actionCode, fields, cancellationToken);

        if (actionCode.Length == 0)
        {
            // Not a reason to stop — C2M still needs an answer, and "not completed" is the honest one.
            logger.LogWarning(
                "Task {TaskNumber} has no '{Field}' answer; closing FA {FaId} as {OpStatus}.",
                task.TaskNumber,
                C2mFieldNames.ActionTaken,
                task.FaId,
                outcome.FaStatus);
        }

        var request = BuildRequest(task, outcome, byName, fields, completionTime);
        var attempt = await db.C2mDispatchLogs.CountAsync(x => x.TaskId == task.Id, cancellationToken) + 1;
        var log = C2mDispatchLog.Start(task.Id, task.FaId, outcome.FaStatus, attempt, JsonSerializer.Serialize(request, LogJson), Now);
        db.C2mDispatchLogs.Add(log);

        var settings = options.Value;
        var skipReason = !settings.Enabled
            ? "C2M integration is disabled (C2m:Enabled)."
            : settings.ByPassClosingInCcb
                ? "Closing in CCB is bypassed (C2m:ByPassClosingInCcb)."
                : null;

        if (skipReason is not null)
        {
            log.Skip(skipReason, Now);
            return C2mDispatchOutcome.Skipped(skipReason);
        }

        await db.SaveChangesAsync(cancellationToken);

        return await SendAsync(task, log, request, timeout, cancellationToken);
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task<C2mDispatchOutcome> SendAsync(
        FieldTask task,
        C2mDispatchLog log,
        C2mClosureRequest request,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        using var deadline = timeout is null ? null : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout is { } window)
        {
            deadline!.CancelAfter(window);
        }

        try
        {
            var response = await client.CloseFieldActivityAsync(request, deadline?.Token ?? cancellationToken);
            var responseJson = JsonSerializer.Serialize(response, LogJson);

            if (!response.IsAccepted)
            {
                var reason = response.ErrorDescription ?? response.ResponseDescription ?? "C2M rejected the closure.";
                log.Fail(responseJson, response.ResponseCode, reason, Now);
                logger.LogError(
                    "C2M rejected closure of FA {FaId}: {Status}/{Code} {Description}",
                    task.FaId,
                    response.Status,
                    response.ResponseCode,
                    reason);
                return C2mDispatchOutcome.Rejected(response.ResponseCode, reason);
            }

            log.Succeed(responseJson, response.ResponseCode, Now);
            logger.LogInformation("C2M accepted closure of FA {FaId} as {OpStatus} for task {TaskNumber}.", task.FaId, log.OpStatus, task.TaskNumber);
            return C2mDispatchOutcome.Accepted(response.ResponseCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our deadline, not the caller giving up. Whether C2M processed it is unknown — which is why
            // the pending row was saved first.
            var message = $"C2M did not answer within {timeout?.TotalSeconds ?? 0:0} seconds.";
            log.Fail(null, null, message, Now);
            logger.LogError("C2M closure of FA {FaId} timed out.", task.FaId);
            return C2mDispatchOutcome.TransportFailed(message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.Fail(null, null, ex.Message, Now);
            logger.LogError(ex, "C2M closure of FA {FaId} failed to send.", task.FaId);
            return C2mDispatchOutcome.TransportFailed(ex.Message);
        }
    }

    private C2mClosureRequest BuildRequest(
        FieldTask task,
        C2mActionOutcome outcome,
        IReadOnlyDictionary<string, object?> answers,
        IReadOnlyList<FormFieldInfo> fields,
        DateTime completionTime)
    {
        var settings = options.Value;
        var comment = AsText(answers.GetValueOrDefault(C2mFieldNames.Remarks));

        return new C2mClosureRequest
        {
            SourceApp = settings.SourceApp,
            TransactionId = task.FaId!,
            FaDetails = new C2mFaDetails
            {
                FaId = task.FaId!,
                FaStatus = outcome.FaStatus,
                MobId = task.WfmTicketId?.ToString(CultureInfo.InvariantCulture),
                CancelReason = outcome.CancelReason,
                ClosureReason = outcome.ClosureReason,
                Comment = comment.Length == 0 ? EmptyComment : comment,
                CompletionDttm = DateTime.SpecifyKind(completionTime, DateTimeKind.Utc).ToString(CompletionDateFormat, CultureInfo.InvariantCulture),
                ImageUrl = string.IsNullOrWhiteSpace(settings.ImageUrlTemplate)
                    ? null
                    : settings.ImageUrlTemplate.Replace(FaIdPlaceholder, task.FaId, StringComparison.Ordinal),
                ParametersList = new C2mParametersList { Parameters = BuildParameters(fields, answers) },
                UserId = settings.UserId,
            },
        };
    }

    /// <summary>A field travels when it names a C2M parameter and was answered.</summary>
    public static IReadOnlyList<C2mParameter> BuildParameters(
        IReadOnlyList<FormFieldInfo> fields,
        IReadOnlyDictionary<string, object?> answers)
    {
        var parameters = new List<C2mParameter>();

        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.C2mParameterName))
            {
                continue;
            }

            var text = AsText(answers.GetValueOrDefault(field.DataName));
            if (text.Length > 0)
            {
                parameters.Add(new C2mParameter { ParameterName = field.C2mParameterName.Trim(), ParameterValue = text });
            }
        }

        return parameters;
    }

    /// <summary>
    /// Compact invariant text, as C2M's samples read: decimals drop trailing zeros (<c>2323.0000</c> →
    /// <c>2323</c>), a yes/no is <c>Y</c>/<c>N</c>, a date <c>yyyy-MM-dd</c>.
    /// </summary>
    public static string AsText(object? value) => value switch
    {
        null or DBNull => "",
        string text => text.Trim(),
        bool flag => flag ? "Y" : "N",
        decimal number => number.ToString("0.################", CultureInfo.InvariantCulture),
        double number => Convert.ToDecimal(number, CultureInfo.InvariantCulture).ToString("0.################", CultureInfo.InvariantCulture),
        float number => Convert.ToDecimal(number, CultureInfo.InvariantCulture).ToString("0.################", CultureInfo.InvariantCulture),
        byte or sbyte or short or ushort or int or uint or long or ulong =>
            Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("0.################", CultureInfo.InvariantCulture),
        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => value.ToString() ?? "",
    };
}
