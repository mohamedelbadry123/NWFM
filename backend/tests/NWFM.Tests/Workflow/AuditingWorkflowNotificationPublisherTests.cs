namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Shared.Integration.Workflow;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Settings;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Domain.Repositories;
using global::Workflow.Infrastructure.Services;

public sealed class AuditingWorkflowNotificationPublisherTests
{
    private static AuditingWorkflowNotificationPublisher BuildPublisher(
        IWorkflowNotificationLogRepository repo,
        IWorkflowSmtpEmailSender? smtpSender = null,
        WorkflowSettings? settings = null)
    {
        var noOpSmtp = new Mock<IWorkflowSmtpEmailSender>();
        noOpSmtp.Setup(s => s.TrySendAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        return new AuditingWorkflowNotificationPublisher(
            repo,
            smtpSender ?? noOpSmtp.Object,
            Options.Create(settings ?? new WorkflowSettings()),
            NullLogger<AuditingWorkflowNotificationPublisher>.Instance);
    }

    [Fact]
    public async Task PublishAsync_PersistsNotificationLog_AsDeliveredForInApp()
    {
        WorkflowNotificationLog? saved = null;
        var repo = new Mock<IWorkflowNotificationLogRepository>();
        repo.Setup(r => r.AddAsync(It.IsAny<WorkflowNotificationLog>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowNotificationLog, CancellationToken>((log, _) => saved = log)
            .Returns(Task.CompletedTask);

        var publisher = BuildPublisher(repo.Object);

        var request = new WorkflowNotificationRequest(
            Guid.NewGuid(),
            "workflow.task.assigned",
            WorkflowNotificationChannel.InApp,
            new Dictionary<string, object?> { ["TaskId"] = "abc" },
            "corr-1",
            new List<Guid> { Guid.NewGuid() });

        await publisher.PublishAsync(request);

        saved.Should().NotBeNull();
        saved!.TemplateKey.Should().Be("workflow.task.assigned");
        saved.OrganizationId.Should().Be(request.OrganizationId);
        saved.CorrelationId.Should().Be("corr-1");
        // No SMTP configured: InApp is delivered via durable log; email stays best-effort.
        saved.Status.Should().Be(WorkflowNotificationLogStatus.Delivered);
        repo.Verify(r => r.AddAsync(It.IsAny<WorkflowNotificationLog>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_LegacyEmailRejectsDiagnosticDelivery()
    {
        var repo = new Mock<IWorkflowNotificationLogRepository>();
        repo.Setup(r => r.AddAsync(It.IsAny<WorkflowNotificationLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var smtpSender = new Mock<IWorkflowSmtpEmailSender>();
        smtpSender.Setup(s => s.TrySendAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var settings = new WorkflowSettings
        {
            Smtp = new WorkflowSmtpSettings { Host = "smtp.privora.sa", FromAddress = "noreply@privora.sa" }
        };

        var publisher = BuildPublisher(repo.Object, smtpSender.Object, settings);

        var request = new WorkflowNotificationRequest(
            Guid.NewGuid(),
            "workflow.task.assigned",
            WorkflowNotificationChannel.Email,
            new Dictionary<string, object?>(),
            "corr-smtp",
            new List<Guid> { Guid.NewGuid() });

        var publish = () => publisher.PublishAsync(request);
        await publish.Should().ThrowAsync<InvalidOperationException>();

        smtpSender.Verify(s => s.TrySendAsync(
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, false, false, false, WorkflowNotificationLogStatus.Delivered)]
    [InlineData(false, true, false, false, WorkflowNotificationLogStatus.Failed)]
    [InlineData(false, true, true, true,  WorkflowNotificationLogStatus.Delivered)]
    [InlineData(true, true, false, false, WorkflowNotificationLogStatus.Failed)]
    [InlineData(true, true, true, false, WorkflowNotificationLogStatus.Failed)]
    [InlineData(true, true, true, true,  WorkflowNotificationLogStatus.Delivered)]
    public void ResolveStatus_MatchesChannelAndSmtp(
        bool inApp, bool email, bool smtp, bool emailDelivered, WorkflowNotificationLogStatus expected)
        => AuditingWorkflowNotificationPublisher.ResolveStatus(email, inApp, smtp, emailDelivered)
            .Should().Be(expected);
}
