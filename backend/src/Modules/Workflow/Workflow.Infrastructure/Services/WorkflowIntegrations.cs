namespace Workflow.Infrastructure.Services;

using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Results;
using Workflow.Application.Integrations;
using Workflow.Application.Settings;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Infrastructure.Persistence;

internal sealed class WorkflowIntegrations(WorkflowDbContext db, ICurrentTenant tenant, IDataProtectionProvider protection,
    WorkflowIntegrationTransport transport, IOptions<WorkflowSettings> settings) : IWorkflowIntegrations, IWorkflowIntegrationRuntime
{
    private IDataProtector Protector => protection.CreateProtector("NWFM.Workflow.Connections.v1", tenant.OrganizationId.ToString());
    internal Dictionary<string, string> Credentials(WorkflowIntegrationConnection c) => string.IsNullOrEmpty(c.ProtectedCredentials)
        ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(Protector.Unprotect(c.ProtectedCredentials)) ?? [];
    private static ConnectionDto Dto(WorkflowIntegrationConnection c) => new(c.Id, c.Name, c.Kind, c.Address,
        c.Authentication, c.ProtectedCredentials.Length > 0, c.AllowPrivateNetwork, c.Port, c.UseTls);
    public async Task<IReadOnlyList<ConnectionDto>> ListConnectionsAsync(CancellationToken ct)
        => (await db.IntegrationConnections.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct)).Select(Dto).ToList();

    public async Task<Result<ConnectionDto>> SaveConnectionAsync(Guid? id, ConnectionInput input, CancellationToken ct)
    {
        Result<ConnectionDto> Invalid(string message) => Result.Failure<ConnectionDto>(new Error("Workflow.Connection.Invalid", message));
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200 || input.Address.Length > 2000) return Invalid("A connection name of at most 200 characters is required.");
        if (!new[] { "Http", "Smtp", "Webhook" }.Contains(input.Kind)) return Invalid("Select an HTTP, SMTP or Webhook connection.");
        if (input.AllowPrivateNetwork && !settings.Value.AllowPrivateConnections) return Invalid("Private network connections must first be enabled in server settings.");
        if (input.Kind == "Http" && (!Uri.TryCreate(input.Address, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0
            || uri.Scheme == "http" && !input.AllowPrivateNetwork)) return Invalid("Enter an HTTPS base URL without credentials, query or fragment.");
        var allowedAuth = input.Kind switch { "Http" => new[] { "None", "Basic", "Bearer", "ApiKey", "OAuth2" }, "Smtp" => ["None", "Basic"], _ => ["ApiKey", "Hmac"] };
        if (!allowedAuth.Contains(input.Authentication)) return Invalid("This authentication method is not supported for the connection type.");
        if (input.Kind == "Smtp" && (string.IsNullOrWhiteSpace(input.Address) || input.Port is < 1 or > 65535)) return Invalid("Enter an SMTP host and a port between 1 and 65535.");
        if (await db.IntegrationConnections.AnyAsync(c => c.Name == input.Name.Trim() && c.Id != id, ct)) return Invalid("A connection with this name already exists.");
        var row = id.HasValue ? await db.IntegrationConnections.FindAsync([id.Value], ct) : null;
        if (id.HasValue && row is null) return Result.Failure<ConnectionDto>(new Error("Workflow.Connection.NotFound", "Connection not found."));
        if (row is not null && row.Kind != input.Kind) return Invalid("A connection type cannot change; create another connection instead.");
        var creds = input.Credentials ?? (row is null ? [] : Credentials(row));
        var required = input.Authentication switch { "Basic" => new[] { "username", "password" }, "Bearer" => ["token"], "ApiKey" => ["apiKey"], "OAuth2" => ["tokenUrl", "clientId", "clientSecret"], "Hmac" => ["secret"], _ => Array.Empty<string>() };
        if (required.Any(k => !creds.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v))) return Invalid("Required authentication fields are missing.");
        if (input.Kind == "Smtp" && !MailAddress.TryCreate(creds.GetValueOrDefault("fromAddress"), out _)) return Invalid("A valid sender email address is required.");
        if (input.Kind == "Webhook" && (creds.GetValueOrDefault(input.Authentication == "Hmac" ? "secret" : "apiKey")?.Length ?? 0) < 32) return Invalid("Webhook credentials must contain at least 32 characters.");
        var encrypted = input.Credentials is null && row is not null ? null : Protector.Protect(JsonSerializer.Serialize(creds));
        row ??= WorkflowIntegrationConnection.Create(tenant.OrganizationId);
        if (!id.HasValue) db.IntegrationConnections.Add(row);
        row.Update(input.Name.Trim(), input.Kind, input.Address.Trim(), input.Authentication, encrypted, input.AllowPrivateNetwork, input.Port, input.UseTls);
        await db.SaveChangesAsync(ct);
        return Result.Success(Dto(row));
    }
    public async Task<Result> DeleteConnectionAsync(Guid id, CancellationToken ct)
    {
        var row = await db.IntegrationConnections.FindAsync([id], ct);
        if (row is null) return Result.Failure(new Error("Workflow.Connection.NotFound", "Connection not found."));
        var idText = id.ToString();
        if (await db.ActivityDefinitions.AnyAsync(a => a.ConfigurationJson != null && a.ConfigurationJson.Contains(idText), ct)
            || await db.IntegrationJobs.AnyAsync(j => j.ConnectionId == id, ct) || await db.EventSubscriptions.AnyAsync(s => s.ConnectionId == id, ct))
            return Result.Failure(new Error("Workflow.Connection.InUse", "This connection is referenced by a workflow or execution and cannot be deleted."));
        db.IntegrationConnections.Remove(row); await db.SaveChangesAsync(ct); return Result.Success();
    }
    public async Task<Result<IntegrationResult>> TestHttpAsync(HttpActivityConfiguration configuration, Dictionary<string, JsonElement> variables, CancellationToken ct)
    {
        var errors = IntegrationConfigurationRules.Validate("Http", JsonSerializer.Serialize(configuration, IntegrationJson.Options));
        if (errors.Count > 0) return Result.Failure<IntegrationResult>(new Error("Workflow.Integration.Invalid", errors[0]));
        var connection = await db.IntegrationConnections.FindAsync([configuration.ConnectionId], ct);
        if (connection?.Kind != "Http") return Result.Failure<IntegrationResult>(new Error("Workflow.Connection.NotFound", "Select an HTTP connection."));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var response = await transport.SendHttpAsync(connection, Credentials(connection), configuration, variables, "test:" + Guid.NewGuid(), ct);
        return Result.Success(response with { ElapsedMilliseconds = clock.ElapsedMilliseconds });
    }
    public async Task<Result<Guid>> ReceiveEventAsync(Guid connectionId, string rawBody, string? timestamp, string? signature, string? apiKey, CancellationToken ct)
    {
        Result<Guid> Invalid(string code, string message) => Result.Failure<Guid>(new Error(code, message));
        if (Encoding.UTF8.GetByteCount(rawBody) > WorkflowIntegrationTransport.MaxResponseBytes) return Invalid("Workflow.Event.TooLarge", "Event body exceeds 256 KB.");
        var connection = await db.IntegrationConnections.FindAsync([connectionId], ct);
        if (connection?.Kind != "Webhook") return Invalid("Workflow.Event.Unauthorized", "Webhook authentication failed.");
        var creds = Credentials(connection);
        bool Equal(string expected, string? actual) => actual is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
        if (connection.Authentication == "ApiKey")
        { if (!Equal(creds.GetValueOrDefault("apiKey") ?? "", apiKey)) return Invalid("Workflow.Event.Unauthorized", "Webhook authentication failed."); }
        else
        {
            if (!long.TryParse(timestamp, out var seconds) || Math.Abs((decimal)DateTimeOffset.UtcNow.ToUnixTimeSeconds() - seconds) > 300) return Invalid("Workflow.Event.Unauthorized", "Webhook timestamp is missing or expired.");
            var hash = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(creds.GetValueOrDefault("secret") ?? ""), Encoding.UTF8.GetBytes(timestamp + "." + rawBody)));
            if (!Equal(hash, signature?.ToLowerInvariant())) return Invalid("Workflow.Event.Unauthorized", "Webhook authentication failed.");
        }
        EventEnvelope envelope;
        try { envelope = JsonSerializer.Deserialize<EventEnvelope>(rawBody, IntegrationJson.Options)!; }
        catch (JsonException) { return Invalid("Workflow.Event.Invalid", "Event body is not valid JSON."); }
        if (envelope is null || string.IsNullOrWhiteSpace(envelope.EventId) || envelope.EventId.Length > 200
            || string.IsNullOrWhiteSpace(envelope.EventKey) || envelope.EventKey.Length > 200
            || string.IsNullOrWhiteSpace(envelope.CorrelationId) || envelope.CorrelationId.Length > 256
            || envelope.Payload.ValueKind is JsonValueKind.Undefined)
            return Invalid("Workflow.Event.Invalid", "Event ID, key, correlation ID and payload are required.");
        return await WorkflowExecutionLock.RunAsync(db, $"receipt:{tenant.OrganizationId}:{connectionId}:{envelope.EventId}", async () =>
        {
            var existing = await db.EventReceipts.FirstOrDefaultAsync(r => r.ConnectionId == connectionId && r.EventId == envelope.EventId, ct);
            if (existing is not null) return Result.Success(existing.Id);
            var row = WorkflowEventReceipt.Create(tenant.OrganizationId, connectionId, envelope.EventId, envelope.EventKey, envelope.CorrelationId, envelope.Payload.GetRawText());
            db.EventReceipts.Add(row); await db.SaveChangesAsync(ct); return Result.Success(row.Id);
        }, ct);
    }
    public async Task<IReadOnlyList<OperationDto>> ListOperationsAsync(Guid? instanceId, CancellationToken ct)
        => await db.IntegrationJobs.AsNoTracking().Where(j => instanceId == null || j.WorkflowInstanceId == instanceId)
            .OrderByDescending(j => j.CreatedAt).Take(200).Select(j => new OperationDto(j.Id, j.WorkflowInstanceId, j.ActivityInstanceId,
                j.Kind, j.Status, j.Attempts, j.NextAttemptAt, j.Error, j.StatusCode, j.CreatedAt)).ToListAsync(ct);
    public async Task<IReadOnlyList<EventReceiptDto>> ListEventsAsync(CancellationToken ct)
        => await db.EventReceipts.AsNoTracking().OrderByDescending(r => r.CreatedAt).Take(200)
            .Select(r => new EventReceiptDto(r.Id, r.EventId, r.EventKey, r.CorrelationId, r.Status, r.ActivityInstanceId, r.Error, r.CreatedAt)).ToListAsync(ct);
    public async Task<IReadOnlyList<EventWaitDto>> ListWaitsAsync(CancellationToken ct)
        => await db.EventSubscriptions.AsNoTracking().OrderByDescending(r => r.CreatedAt).Take(200)
            .Select(r => new EventWaitDto(r.Id, r.WorkflowInstanceId, r.ActivityInstanceId, r.EventKey, r.CorrelationId, r.Status, r.ExpiresAt)).ToListAsync(ct);
    public Task<Result> ReplayEventAsync(Guid receiptId, Guid? activityId, CancellationToken ct)
        => WorkflowExecutionLock.RunAsync(db, "event-matching", async () =>
        {
            var receipt = await db.EventReceipts.FindAsync([receiptId], ct);
            if (receipt is null || receipt.Status is not ("Ambiguous" or "Rejected" or "Received"))
                return Result.Failure(new Error("Workflow.Event.NotReplayable", "Only unconsumed events can be replayed."));
            var matches = await db.EventSubscriptions.Where(s => s.Status == "Waiting" && s.ConnectionId == receipt.ConnectionId
                && s.EventKey == receipt.EventKey && s.CorrelationId == receipt.CorrelationId && s.ExpiresAt >= receipt.CreatedAt
                && (activityId == null || s.ActivityInstanceId == activityId)).ToListAsync(ct);
            if (matches.Count != 1) return Result.Failure(new Error("Workflow.Event.Ambiguous", "Select exactly one matching active wait using its activity ID."));
            receipt.Finish("Received", matches[0].ActivityInstanceId); await db.SaveChangesAsync(ct); return Result.Success();
        }, ct);
    public async Task<Result> ReplayOperationAsync(Guid operationId, CancellationToken ct)
    {
        var instanceId = await db.IntegrationJobs.AsNoTracking().Where(j => j.Id == operationId).Select(j => (Guid?)j.WorkflowInstanceId).FirstOrDefaultAsync(ct);
        return await WorkflowExecutionLock.RunAsync(db, "instance:" + instanceId, async () =>
        {
            var job = await db.IntegrationJobs.FindAsync([operationId], ct);
            if (job is null || job.Status != "Failed") return Result.Failure(new Error("Workflow.Job.NotReplayable", "Only failed operations can be replayed."));
            var instance = await db.WorkflowInstances.FindAsync([job.WorkflowInstanceId], ct);
            var activity = await db.ActivityInstances.FindAsync([job.ActivityInstanceId], ct);
            if (job.IsActivityEvent)
            {
                if (instance is null || activity is null || !await WorkflowTreeGuard.CanRunAsync(db, instance.Id, ct, !job.Required)
                    || job.Required && activity.Status != ActivityInstanceStatus.Active)
                    return Result.Failure(new Error("Workflow.Job.NotReplayable", "This event no longer belongs to an active execution."));
                job.Replay(); await db.SaveChangesAsync(ct); return Result.Success();
            }
            if (instance is null || activity is null || instance.Status is WorkflowInstanceStatus.Cancelled or WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Suspended
                || activity.Status != ActivityInstanceStatus.Failed)
                return Result.Failure(new Error("Workflow.Job.NotReplayable", "The workflow or activity can no longer replay this operation."));
            instance.Resume(DateTime.UtcNow); activity.Reopen(); job.Replay(); await db.SaveChangesAsync(ct); return Result.Success();
        }, ct);
    }

    public async Task<Result> QueueAsync(Guid instanceId, Guid activityId, string kind, string configurationJson,
        IReadOnlyDictionary<string, object?> variables, string operationKey, CancellationToken ct)
    {
        try
        {
            var errors = IntegrationConfigurationRules.Validate(kind, configurationJson);
            if (errors.Count > 0) return Result.Failure(new Error("Workflow.Integration.Invalid", errors[0]));
            var connectionId = kind == "Http" ? IntegrationJson.Read<HttpActivityConfiguration>(configurationJson).ConnectionId : IntegrationJson.Read<EmailActivityConfiguration>(configurationJson).ConnectionId;
            var connection = await db.IntegrationConnections.FindAsync([connectionId], ct);
            if (connection?.Kind != (kind == "Http" ? "Http" : "Smtp")) return Result.Failure(new Error("Workflow.Connection.NotFound", "The configured integration connection is unavailable."));
            if (kind == "Email")
            {
                var config = IntegrationJson.Read<EmailActivityConfiguration>(configurationJson);
                var recipients = await db.Participants.Where(p => p.IsActive && config.RecipientUserIds.Contains(p.UserId)).Select(p => p.Email).ToListAsync(ct);
                config.To = string.Join(",", new[] { config.To }.Concat(recipients).Where(s => !string.IsNullOrWhiteSpace(s)));
                configurationJson = JsonSerializer.Serialize(config, IntegrationJson.Options);
            }
            if (await db.IntegrationJobs.AnyAsync(j => j.OperationKey == operationKey, ct)) return Result.Success();
            db.IntegrationJobs.Add(WorkflowIntegrationJob.Create(tenant.OrganizationId, instanceId, activityId, connectionId,
                kind, configurationJson, JsonSerializer.Serialize(variables), operationKey));
            await db.SaveChangesAsync(ct); return Result.Success();
        }
        catch (JsonException) { return Result.Failure(new Error("Workflow.Integration.Invalid", "Integration configuration is invalid.")); }
    }
    public async Task<Result> RegisterWaitAsync(Guid instanceId, Guid activityId, string configurationJson,
        IReadOnlyDictionary<string, object?> variables, CancellationToken ct)
    {
        var errors = IntegrationConfigurationRules.Validate("Webhook", configurationJson);
        if (errors.Count > 0) return Result.Failure(new Error("Workflow.Event.Invalid", errors[0]));
        var config = IntegrationJson.Read<EventActivityConfiguration>(configurationJson);
        var connection = await db.IntegrationConnections.FindAsync([config.ConnectionId], ct);
        if (connection?.Kind != "Webhook") return Result.Failure(new Error("Workflow.Connection.NotFound", "Select a webhook connection for this event."));
        if (!variables.TryGetValue(config.CorrelationVariable, out var correlation) || correlation is null)
            return Result.Failure(new Error("Workflow.Event.CorrelationMissing", "The correlation variable must have a value before waiting for an event."));
        var correlationValue = correlation is JsonElement element ? IntegrationValueMapper.Scalar(element) : Convert.ToString(correlation, System.Globalization.CultureInfo.InvariantCulture)!;
        if (string.IsNullOrWhiteSpace(correlationValue) || correlationValue.Length > 256) return Result.Failure(new Error("Workflow.Event.CorrelationInvalid", "The event correlation value must contain 1 to 256 characters."));
        db.EventSubscriptions.Add(WorkflowEventSubscription.Create(tenant.OrganizationId, instanceId, activityId,
            config.ConnectionId, config.EventKey, correlationValue, configurationJson, DateTime.UtcNow.AddSeconds(Math.Clamp(config.TimeoutSeconds, 1, 31536000))));
        await db.SaveChangesAsync(ct); return Result.Success();
    }

    public async Task<IReadOnlyList<Workflow.Application.DTOs.WorkflowValidationIssueDto>> ValidateConnectionsAsync(Workflow.Application.Models.WorkflowXmlDocument document, CancellationToken ct)
    {
        var issues = new List<Workflow.Application.DTOs.WorkflowValidationIssueDto>();
        foreach (var activity in document.Activities)
        {
            try
            {
                using var json = JsonDocument.Parse(activity.ConfigurationJson ?? "{}");
                if (activity.ActivityTypeName == "CallActivity")
                {
                    var childKey = json.RootElement.TryGetProperty("definitionKey", out var key) && key.ValueKind == JsonValueKind.String ? key.GetString() : null;
                    var child = await db.WorkflowDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.DefinitionKey == childKey, ct);
                    Guid? fixedVersion = json.RootElement.TryGetProperty("versionId", out var childVersion) && childVersion.ValueKind == JsonValueKind.String && childVersion.TryGetGuid(out var pinned) ? pinned : null;
                    if (child is null || !await db.WorkflowVersions.AnyAsync(v => v.WorkflowDefinitionId == child.Id && v.Status == WorkflowVersionStatus.Published && (fixedVersion == null || v.Id == fixedVersion), ct))
                        issues.Add(new("CHILD_VERSION_UNAVAILABLE", "Select a child definition with an available published version in this organization.", activity.NodeKey));
                    continue;
                }
                if (!json.RootElement.TryGetProperty("connectionId", out var property) || !property.TryGetGuid(out var id) || id == Guid.Empty) continue;
                var kind = activity.ActivityTypeName switch { "ServiceTask" => "Http", "WaitEvent" => "Webhook", "NotificationTask" => "Smtp", _ => null };
                if (kind is null) continue;
                var connection = await db.IntegrationConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
                if (connection?.Kind != kind) issues.Add(new("CONNECTION_UNAVAILABLE", $"Select an available {kind} connection in this organization.", activity.NodeKey));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { /* Structural validator reports invalid JSON. */ }
        }
        return issues;
    }
}
