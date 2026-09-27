namespace NWFM.Tests.Modules.Workflow;

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using global::Workflow.Application.Integrations;
using global::Workflow.Application.Settings;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Services;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [Theory]
    [InlineData("true")]
    [InlineData("150")]
    [InlineData("a\"b")]
    [InlineData("line\nnext")]
    public async Task TypedAssignments_PreserveLiteralStrings(string value)
    {
        var config = Config(new { assignmentFormat = "typed", setVariables = new Dictionary<string,string> { ["BusinessEntityId"] = value } });
        var (binding, version) = SeedSingleActivity(ActivityType.ScriptTask, config, DateTime.UtcNow);
        var started = await BuildEngine(_db).StartAsync(_orgId, binding.Id, "original", "typed", DateTime.UtcNow, pinnedWorkflowVersionId: version);
        started.IsSuccess.Should().BeTrue();
        JsonSerializer.Deserialize<string>((await _db.WorkflowVariables.SingleAsync(v => v.VariableName == "BusinessEntityId")).ValueJson!).Should().Be(value);
    }

    private WorkflowIntegrations Integrations() => new(_db, new StubTenant(_orgId), new EphemeralDataProtectionProvider(),
        new WorkflowIntegrationTransport(), Options.Create(new WorkflowSettings { AllowPrivateConnections = true }));
    private async Task<Guid> Connection(WorkflowIntegrations api, string kind = "Webhook", string address = "", string auth = "ApiKey")
    {
        var result = await api.SaveConnectionAsync(null, new("test-" + Guid.NewGuid(), kind, address, auth,
            auth == "ApiKey" ? new() { ["apiKey"] = new string('a', 40) } : auth == "Hmac" ? new() { ["secret"] = new string('b', 40) } : [], kind == "Http"), default);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : ""); return result.Value.Id;
    }
    private static string Config(object value) => JsonSerializer.Serialize(value, IntegrationJson.Options);
    private static string Envelope(string id, string correlation = "order-1", string payload = "{\"approved\":true}")
        => $$"""{"eventId":"{{id}}","eventKey":"ready","correlationId":"{{correlation}}","payload":{{payload}}} """;

    [Fact]
    public async Task Credentials_AreEncrypted_NeverReturned_AndPreservedOnEdit()
    {
        var api = Integrations(); var id = await Connection(api);
        var row = await _db.IntegrationConnections.FindAsync(id);
        row!.ProtectedCredentials.Should().NotContain(new string('a', 40));
        Config(await api.ListConnectionsAsync(default)).Should().NotContain(new string('a', 40));
        (await api.SaveConnectionAsync(id, new("renamed", "Webhook", "", "ApiKey"), default)).IsSuccess.Should().BeTrue();
        (await api.ReceiveEventAsync(id, Envelope("event-1"), null, null, new string('a', 40), default)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Callback_AuthenticatesBeforeParsing_Deduplicates_AndMapsEarlyEvent()
    {
        var api = Integrations(); var connection = await Connection(api);
        (await api.ReceiveEventAsync(connection, "not json", null, null, "bad", default)).Error.Code.Should().EndWith("Unauthorized");
        var first = await api.ReceiveEventAsync(connection, Envelope("event-1"), null, null, new string('a', 40), default);
        var duplicate = await api.ReceiveEventAsync(connection, Envelope("event-1"), null, null, new string('a', 40), default);
        duplicate.Value.Should().Be(first.Value);
        var (binding, version) = SeedSingleActivity(ActivityType.WaitEvent, Config(new EventActivityConfiguration
        { ConnectionId = connection, EventKey = "ready", OutputMappings = new() { ["approved"] = "approved" } }), DateTime.UtcNow);
        var engine = BuildEngine(_db, api);
        var start = await engine.StartAsync(_orgId, binding.Id, "order-1", "event-flow", DateTime.UtcNow, correlationId: "order-1", pinnedWorkflowVersionId: version);
        start.IsSuccess.Should().BeTrue();
        var processor = new WorkflowIntegrationProcessor(_db, api, new(), engine);
        await processor.ProcessEventsAsync(default); await processor.ProcessEventsAsync(default);
        start.Value.Status.Should().Be(WorkflowInstanceStatus.Completed);
        (await _db.EventReceipts.SingleAsync()).Status.Should().Be("Processed");
        (await _db.WorkflowVariables.SingleAsync(v => v.VariableName == "approved")).ValueJson.Should().Be("true");
        (await _db.ActivityInstances.CountAsync(a => a.ActivityType == ActivityType.End)).Should().Be(1);
    }

    [Fact]
    public async Task Hmac_RejectsTamperingAndExpiredSignatures()
    {
        var api = Integrations(); var id = await Connection(api, auth: "Hmac"); var body = Envelope("signed");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string Sign(string stamp) => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(new string('b', 40)), Encoding.UTF8.GetBytes(stamp + "." + body)));
        (await api.ReceiveEventAsync(id, body + " ", timestamp, Sign(timestamp), null, default)).IsFailure.Should().BeTrue();
        var old = DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeSeconds().ToString();
        (await api.ReceiveEventAsync(id, body, old, Sign(old), null, default)).IsFailure.Should().BeTrue();
        (await api.ReceiveEventAsync(id, body, timestamp, Sign(timestamp), null, default)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Callback_DoesNotBypassSourceAuthenticationThroughInternalSignal()
    {
        var api = Integrations(); var connection = await Connection(api);
        var (binding, version) = SeedSingleActivity(ActivityType.WaitEvent, Config(new EventActivityConfiguration { ConnectionId = connection, EventKey = "ready" }), DateTime.UtcNow);
        var engine = BuildEngine(_db, api);
        var start = await engine.StartAsync(_orgId, binding.Id, "order", "source", DateTime.UtcNow, pinnedWorkflowVersionId: version);
        (await engine.ResumeFromExternalSignalAsync(start.Value.Id, "ready", DateTime.UtcNow)).IsFailure.Should().BeTrue();
        start.Value.Status.Should().Be(WorkflowInstanceStatus.Running);
    }

    [Fact]
    public async Task SuspendedWait_RetainsEvent_ResumesOnce_AndCancelledWaitNeverAdvances()
    {
        var api = Integrations(); var connection = await Connection(api);
        var (binding, version) = SeedSingleActivity(ActivityType.WaitEvent, Config(new EventActivityConfiguration { ConnectionId = connection, EventKey = "ready" }), DateTime.UtcNow);
        var engine = BuildEngine(_db, api); var processor = new WorkflowIntegrationProcessor(_db, api, new(), engine);
        var start = await engine.StartAsync(_orgId, binding.Id, "one", "suspended", DateTime.UtcNow, "order-1", pinnedWorkflowVersionId: version);
        start.Value.Suspend(DateTime.UtcNow); await _db.SaveChangesAsync();
        await api.ReceiveEventAsync(connection, Envelope("during-suspend"), null, null, new string('a', 40), default);
        await processor.ProcessEventsAsync(default);
        (await _db.EventReceipts.SingleAsync()).Status.Should().Be("Received");
        start.Value.Resume(DateTime.UtcNow); await _db.SaveChangesAsync(); await processor.ProcessEventsAsync(default);
        start.Value.Status.Should().Be(WorkflowInstanceStatus.Completed);
        var second = await engine.StartAsync(_orgId, binding.Id, "two", "cancelled", DateTime.UtcNow, "order-2", pinnedWorkflowVersionId: version);
        second.Value.Cancel(DateTime.UtcNow); await _db.SaveChangesAsync();
        await api.ReceiveEventAsync(connection, Envelope("cancel", "order-2"), null, null, new string('a', 40), default);
        await processor.ProcessEventsAsync(default);
        second.Value.Status.Should().Be(WorkflowInstanceStatus.Cancelled);
        (await _db.EventReceipts.SingleAsync(r => r.EventId == "cancel")).Status.Should().Be("Rejected");
    }

    [Fact]
    public async Task AmbiguousEvent_RequiresExplicitTarget_AndDoesNotConsumeBothWaits()
    {
        var api = Integrations(); var connection = await Connection(api);
        var (binding, version) = SeedSingleActivity(ActivityType.WaitEvent, Config(new EventActivityConfiguration { ConnectionId = connection, EventKey = "ready" }), DateTime.UtcNow);
        var engine = BuildEngine(_db, api); var processor = new WorkflowIntegrationProcessor(_db, api, new(), engine);
        var first = await engine.StartAsync(_orgId, binding.Id, "1", "amb-1", DateTime.UtcNow, "order-1", pinnedWorkflowVersionId: version);
        await engine.StartAsync(_orgId, binding.Id, "2", "amb-2", DateTime.UtcNow, "order-1", pinnedWorkflowVersionId: version);
        var receipt = await api.ReceiveEventAsync(connection, Envelope("ambiguous"), null, null, new string('a', 40), default);
        await processor.ProcessEventsAsync(default);
        (await _db.EventReceipts.FindAsync(receipt.Value))!.Status.Should().Be("Ambiguous");
        var target = await _db.EventSubscriptions.SingleAsync(s => s.WorkflowInstanceId == first.Value.Id);
        (await api.ReplayEventAsync(receipt.Value, target.ActivityInstanceId, default)).IsSuccess.Should().BeTrue();
        await processor.ProcessEventsAsync(default);
        (await _db.EventSubscriptions.CountAsync(s => s.Status == "Received")).Should().Be(1);
        (await api.ReplayEventAsync(receipt.Value, target.ActivityInstanceId, default)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Timeout_WithoutErrorRoute_FailsRatherThanFollowingSuccess()
    {
        var api = Integrations(); var connection = await Connection(api);
        var (binding, version) = SeedSingleActivity(ActivityType.WaitEvent, Config(new EventActivityConfiguration { ConnectionId = connection, EventKey = "ready" }), DateTime.UtcNow);
        var engine = BuildEngine(_db, api); var start = await engine.StartAsync(_orgId, binding.Id, "1", "timeout", DateTime.UtcNow, pinnedWorkflowVersionId: version);
        var wait = await _db.EventSubscriptions.SingleAsync();
        _db.Entry(wait).Property(nameof(wait.ExpiresAt)).CurrentValue = DateTime.UtcNow.AddSeconds(-1); await _db.SaveChangesAsync();
        await new WorkflowIntegrationProcessor(_db, api, new(), engine).ProcessEventsAsync(default);
        start.Value.Status.Should().Be(WorkflowInstanceStatus.Failed);
        wait.Status.Should().Be("TimedOut");
        (await _db.ActivityInstances.AnyAsync(a => a.ActivityType == ActivityType.End)).Should().BeFalse();
    }

    [Fact]
    public async Task PersistedHttpDelivery_RecoversWithoutSendingAgain()
    {
        var api = Integrations(); var connection = await Connection(api, "Http", "http://127.0.0.1:1", "None");
        var (binding, version) = SeedSingleActivity(ActivityType.ServiceTask, Config(new HttpActivityConfiguration { ConnectionId = connection, OutputMappings = new() { ["externalId"] = "body.id" } }), DateTime.UtcNow, "http.request");
        var engine = BuildEngine(_db, api); var start = await engine.StartAsync(_orgId, binding.Id, "1", "saved-result", DateTime.UtcNow, pinnedWorkflowVersionId: version);
        var job = await _db.IntegrationJobs.SingleAsync(); job.RecordDelivery(Config(new IntegrationResult(true, 201, "{\"id\":42}", null)), 201); await _db.SaveChangesAsync();
        await new WorkflowIntegrationProcessor(_db, api, new(), engine).ProcessJobAsync(job.Id, default);
        job.Status.Should().Be("Completed"); job.Attempts.Should().Be(0);
        start.Value.Status.Should().Be(WorkflowInstanceStatus.Completed);
        (await _db.WorkflowVariables.SingleAsync(v => v.VariableName == "externalId")).ValueJson.Should().Be("42");
    }

    [Fact]
    public async Task FailedDelivery_ReplayReopensOnlyItsActivity_AndPreservesOperationIdentity()
    {
        var api = Integrations(); var connection = await Connection(api, "Http", "http://127.0.0.1:1", "None");
        var (binding, version) = SeedSingleActivity(ActivityType.ServiceTask, Config(new HttpActivityConfiguration { ConnectionId = connection }), DateTime.UtcNow, "http.request");
        var engine = BuildEngine(_db, api); var start = await engine.StartAsync(_orgId, binding.Id, "1", "replay", DateTime.UtcNow, pinnedWorkflowVersionId: version);
        var job = await _db.IntegrationJobs.SingleAsync(); var operationKey = job.OperationKey;
        job.RecordDelivery(Config(new IntegrationResult(false, 503, "", "Unavailable")), 503); await _db.SaveChangesAsync();
        var processor = new WorkflowIntegrationProcessor(_db, api, new(), engine);
        await processor.ProcessJobAsync(job.Id, default);
        job.Status.Should().Be("Failed"); start.Value.Status.Should().Be(WorkflowInstanceStatus.Failed);
        (await api.ReplayOperationAsync(job.Id, default)).IsSuccess.Should().BeTrue();
        job.OperationKey.Should().Be(operationKey); start.Value.Status.Should().Be(WorkflowInstanceStatus.Running);
        job.RecordDelivery(Config(new IntegrationResult(true, 200, "{}", null)), 200); await _db.SaveChangesAsync();
        await processor.ProcessJobAsync(job.Id, default);
        job.Status.Should().Be("Completed"); start.Value.Status.Should().Be(WorkflowInstanceStatus.Completed);
        (await api.ReplayOperationAsync(job.Id, default)).IsFailure.Should().BeTrue();
        (await _db.ActivityInstances.CountAsync(a => a.ActivityType == ActivityType.End)).Should().Be(1);
    }

    [Fact]
    public async Task Http_UsesRealRequest_MapsHeaders_RedactsCredentials_AndRefusesRedirect()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = Task.Run(async () => { using var client = await listener.AcceptTcpClientAsync(); using var stream = client.GetStream(); using var reader = new StreamReader(stream, leaveOpen: true);
            var lines = new List<string>(); string? line; while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync())) lines.Add(line);
            var response = Encoding.UTF8.GetBytes("HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:1/secret\r\nX-Result: ok\r\nContent-Length: 12\r\nConnection: close\r\n\r\nsecret-token"); await stream.WriteAsync(response); return lines; });
        var connection = WorkflowIntegrationConnection.Create(_orgId); connection.Update("local", "Http", $"http://127.0.0.1:{port}", "Bearer", "", true, 0, false);
        var result = await new WorkflowIntegrationTransport().SendHttpAsync(connection, new() { ["token"] = "secret-token" },
            new() { Path = "/orders", Query = new() { ["id"] = "{{id}}" } }, new() { ["id"] = JsonSerializer.SerializeToElement("A B") }, "operation-1", default);
        result.StatusCode.Should().Be(302); result.Success.Should().BeFalse(); result.Body.Should().Be("[redacted]"); result.Headers!["x-result"].Should().Be("ok");
        var request = await received; request.Should().Contain("GET /orders?id=A%20B HTTP/1.1"); request.Should().Contain("Idempotency-Key: operation-1");
    }

    [Theory]
    [InlineData("127.0.0.1", false, false)] [InlineData("10.0.0.1", false, false)]
    [InlineData("169.254.169.254", true, false)] [InlineData("::ffff:127.0.0.1", false, false)]
    [InlineData("::1", false, false)] [InlineData("fc00::1", false, false)]
    [InlineData("8.8.8.8", false, true)] [InlineData("10.0.0.1", true, true)]
    public void Http_NetworkPolicyRejectsPrivateAndMetadata(string address, bool allowPrivate, bool allowed)
        => WorkflowIntegrationTransport.IsAllowedAddress(IPAddress.Parse(address), allowPrivate).Should().Be(allowed);

    [Fact]
    public void JsonTemplates_PreserveTypesAndEscapeInput_AndMappingsRejectMissingFields()
    {
        var rendered = IntegrationValueMapper.RenderJson("""{"name":"{{name}}","amount":"{{amount}}"}""",
            new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement("a\"b"), ["amount"] = JsonSerializer.SerializeToElement(42) });
        using var json = JsonDocument.Parse(rendered);
        json.RootElement.GetProperty("name").GetString().Should().Be("a\"b"); json.RootElement.GetProperty("amount").GetInt32().Should().Be(42);
        var action = () => IntegrationValueMapper.Map("{}", new() { ["id"] = "missing.id" }); action.Should().Throw<InvalidOperationException>();
    }
}
