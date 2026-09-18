using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Workflow");

            migrationBuilder.EnsureSchema(
                name: "Tenancy");

            migrationBuilder.CreateTable(
                name: "activity_instances",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityNodeKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ActivityType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activity_instances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "business_calendars",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TimeZone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_calendars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sla_policies",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PolicyCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Duration = table.Column<int>(type: "int", nullable: false),
                    DurationUnit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BusinessCalendarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReminderThresholdsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    EscalationThresholdsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    EscalationAssignmentKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sla_policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                schema: "Tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "transition_instances",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromActivityNodeKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ToActivityNodeKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TransitionKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ConditionExpression = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    WasDefault = table.Column<bool>(type: "bit", nullable: false),
                    TakenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transition_instances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "work_item_candidates",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CandidateType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferenceKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_item_candidates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "work_items",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClaimedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DueAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    ActionTaken = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CommentText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_assignment_groups",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AssignmentStrategy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_assignment_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_binding_assignment_mappings",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowBindingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AssignmentGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_binding_assignment_mappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_bindings",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModuleKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TriggerEvent = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Disabled"),
                    VersionPolicy = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Latest"),
                    ExecutionPolicy = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "StartNewInstance"),
                    FixedWorkflowVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartEventKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StartConditionExpression = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ScreenKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    InputMappingJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OutcomeMappingJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConditionJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_bindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_definitions",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_departments",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DefaultAssignmentGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_events",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ActivityNodeKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_execution_tokens",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParallelGatewayNodeKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BranchKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Active"),
                    JoinNodeKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ParentTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_execution_tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_incidents",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActivityNodeKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IncidentType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Open"),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IgnoredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IgnoredByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_incidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_instances",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowBindingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PinnedWorkflowVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BusinessEntityId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SuspendedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    StartedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentActivityNodeKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ParentInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ParentActivityNodeKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_instances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_integration_inbox",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ModuleKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BusinessEntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BusinessEntityId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TriggerEvent = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    BindingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_integration_inbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_integration_outbox",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    BindingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModuleKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BusinessEntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BusinessEntityId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OutcomeKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_integration_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_notification_logs",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Channels = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecipientsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VariablesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Logged"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_notification_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_participants",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_participants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_requests",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    WorkflowBindingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessEntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BusinessEntityId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ServiceKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ServiceNameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ServiceNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ScreenKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TriggerEventKey = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    RequestDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequesterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Running"),
                    CurrentActivityInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentActivityNameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CurrentActivityNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OriginalAssignedGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentAssignedGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentTaskSlaMinutes = table.Column<int>(type: "int", nullable: true),
                    CurrentTaskDueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CurrentClaimedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_timers",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimerType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DueAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SignalKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_timers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_variables",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariableName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ValueJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DataType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_variables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_versions",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    XmlContent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    XmlHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SchemaVersion = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DesignerJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ValidationStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ValidationResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangeSummary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "business_calendar_holidays",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HolidayDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsRecurring = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_calendar_holidays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_business_calendar_holidays_business_calendars_CalendarId",
                        column: x => x.CalendarId,
                        principalSchema: "Workflow",
                        principalTable: "business_calendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "business_calendar_periods",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    IsWorkingTime = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_calendar_periods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_business_calendar_periods_business_calendars_CalendarId",
                        column: x => x.CalendarId,
                        principalSchema: "Workflow",
                        principalTable: "business_calendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_department_members",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_department_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_department_members_workflow_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "Workflow",
                        principalTable: "workflow_departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_workflow_department_members_workflow_participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalSchema: "Workflow",
                        principalTable: "workflow_participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_group_members",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CanClaim = table.Column<bool>(type: "bit", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_group_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_group_members_workflow_assignment_groups_AssignmentGroupId",
                        column: x => x.AssignmentGroupId,
                        principalSchema: "Workflow",
                        principalTable: "workflow_assignment_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_workflow_group_members_workflow_participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalSchema: "Workflow",
                        principalTable: "workflow_participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_activity_definitions",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ActivityType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ActionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PositionX = table.Column<double>(type: "float", nullable: true),
                    PositionY = table.Column<double>(type: "float", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_activity_definitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_activity_definitions_workflow_versions_WorkflowVersionId",
                        column: x => x.WorkflowVersionId,
                        principalSchema: "Workflow",
                        principalTable: "workflow_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_transitions",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromActivityDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToActivityDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ConditionExpression = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_transitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_transitions_workflow_versions_WorkflowVersionId",
                        column: x => x.WorkflowVersionId,
                        principalSchema: "Workflow",
                        principalTable: "workflow_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_variable_definitions",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariableKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DataType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    DefaultValue = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsSensitive = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_variable_definitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_variable_definitions_workflow_versions_WorkflowVersionId",
                        column: x => x.WorkflowVersionId,
                        principalSchema: "Workflow",
                        principalTable: "workflow_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_activity_action_definitions",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExecutionTrigger = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OutcomeKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ConditionExpression = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    InputMappingJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OutputMappingJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailurePolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    RetryDelaySeconds = table.Column<int>(type: "int", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_activity_action_definitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_activity_action_definitions_workflow_activity_definitions_ActivityDefinitionId",
                        column: x => x.ActivityDefinitionId,
                        principalSchema: "Workflow",
                        principalTable: "workflow_activity_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_workflow_activity_action_definitions_workflow_versions_WorkflowVersionId",
                        column: x => x.WorkflowVersionId,
                        principalSchema: "Workflow",
                        principalTable: "workflow_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_activity_assignment_rules",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssigneeType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AssignmentPurpose = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AssignmentKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Expression = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsFallback = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_activity_assignment_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_activity_assignment_rules_workflow_activity_definitions_ActivityDefinitionId",
                        column: x => x.ActivityDefinitionId,
                        principalSchema: "Workflow",
                        principalTable: "workflow_activity_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_activity_outcome_definitions",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutcomeKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    RequiresComment = table.Column<bool>(type: "bit", nullable: false),
                    RequiresAttachment = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ResultValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_activity_outcome_definitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_activity_outcome_definitions_workflow_activity_definitions_ActivityDefinitionId",
                        column: x => x.ActivityDefinitionId,
                        principalSchema: "Workflow",
                        principalTable: "workflow_activity_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_workflow_activity_outcome_definitions_workflow_versions_WorkflowVersionId",
                        column: x => x.WorkflowVersionId,
                        principalSchema: "Workflow",
                        principalTable: "workflow_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_activity_instances_OrganizationId",
                schema: "Workflow",
                table: "activity_instances",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_activity_instances_WorkflowInstanceId",
                schema: "Workflow",
                table: "activity_instances",
                column: "WorkflowInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_business_calendar_holidays_CalendarId_HolidayDate",
                schema: "Workflow",
                table: "business_calendar_holidays",
                columns: new[] { "CalendarId", "HolidayDate" });

            migrationBuilder.CreateIndex(
                name: "IX_business_calendar_periods_CalendarId_DayOfWeek",
                schema: "Workflow",
                table: "business_calendar_periods",
                columns: new[] { "CalendarId", "DayOfWeek" });

            migrationBuilder.CreateIndex(
                name: "IX_business_calendars_Code",
                schema: "Workflow",
                table: "business_calendars",
                column: "Code",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_business_calendars_OrganizationId",
                schema: "Workflow",
                table: "business_calendars",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_sla_policies_BusinessCalendarId",
                schema: "Workflow",
                table: "sla_policies",
                column: "BusinessCalendarId");

            migrationBuilder.CreateIndex(
                name: "IX_sla_policies_OrganizationId",
                schema: "Workflow",
                table: "sla_policies",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_sla_policies_PolicyCode",
                schema: "Workflow",
                table: "sla_policies",
                column: "PolicyCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transition_instances_OrganizationId",
                schema: "Workflow",
                table: "transition_instances",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_transition_instances_WorkflowInstanceId",
                schema: "Workflow",
                table: "transition_instances",
                column: "WorkflowInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_work_item_candidates_OrganizationId",
                schema: "Workflow",
                table: "work_item_candidates",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_work_item_candidates_WorkItemId",
                schema: "Workflow",
                table: "work_item_candidates",
                column: "WorkItemId");

            migrationBuilder.CreateIndex(
                name: "IX_work_items_OrganizationId_AssignmentGroupId_Status",
                schema: "Workflow",
                table: "work_items",
                columns: new[] { "OrganizationId", "AssignmentGroupId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_work_items_OrganizationId_ClaimedByUserId_Status",
                schema: "Workflow",
                table: "work_items",
                columns: new[] { "OrganizationId", "ClaimedByUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_work_items_WorkflowInstanceId",
                schema: "Workflow",
                table: "work_items",
                column: "WorkflowInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_activity_action_definitions_ActivityDefinitionId",
                schema: "Workflow",
                table: "workflow_activity_action_definitions",
                column: "ActivityDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_activity_action_definitions_WorkflowVersionId_ActivityDefinitionId_ActionKey",
                schema: "Workflow",
                table: "workflow_activity_action_definitions",
                columns: new[] { "WorkflowVersionId", "ActivityDefinitionId", "ActionKey" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_activity_action_definitions_WorkflowVersionId_ActivityDefinitionId_Sequence",
                schema: "Workflow",
                table: "workflow_activity_action_definitions",
                columns: new[] { "WorkflowVersionId", "ActivityDefinitionId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_activity_assignment_rules_ActivityDefinitionId",
                schema: "Workflow",
                table: "workflow_activity_assignment_rules",
                column: "ActivityDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_activity_definitions_WorkflowVersionId_NodeKey",
                schema: "Workflow",
                table: "workflow_activity_definitions",
                columns: new[] { "WorkflowVersionId", "NodeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_activity_outcome_definitions_ActivityDefinitionId",
                schema: "Workflow",
                table: "workflow_activity_outcome_definitions",
                column: "ActivityDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_activity_outcome_definitions_WorkflowVersionId_ActivityDefinitionId_OutcomeKey",
                schema: "Workflow",
                table: "workflow_activity_outcome_definitions",
                columns: new[] { "WorkflowVersionId", "ActivityDefinitionId", "OutcomeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_assignment_groups_OrganizationId",
                schema: "Workflow",
                table: "workflow_assignment_groups",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_assignment_groups_OrganizationId_Code",
                schema: "Workflow",
                table: "workflow_assignment_groups",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_binding_assignment_mappings_OrganizationId",
                schema: "Workflow",
                table: "workflow_binding_assignment_mappings",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_binding_assignment_mappings_OrganizationId_WorkflowBindingId_AssignmentKey",
                schema: "Workflow",
                table: "workflow_binding_assignment_mappings",
                columns: new[] { "OrganizationId", "WorkflowBindingId", "AssignmentKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_binding_assignment_mappings_WorkflowBindingId",
                schema: "Workflow",
                table: "workflow_binding_assignment_mappings",
                column: "WorkflowBindingId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_bindings_OrganizationId",
                schema: "Workflow",
                table: "workflow_bindings",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_bindings_WorkflowDefinitionId",
                schema: "Workflow",
                table: "workflow_bindings",
                column: "WorkflowDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_bindings_WorkflowDefinitionId_OrganizationId_ModuleKey_EntityType_TriggerEvent",
                schema: "Workflow",
                table: "workflow_bindings",
                columns: new[] { "WorkflowDefinitionId", "OrganizationId", "ModuleKey", "EntityType", "TriggerEvent" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_definitions_OrganizationId",
                schema: "Workflow",
                table: "workflow_definitions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_definitions_OrganizationId_DefinitionKey",
                schema: "Workflow",
                table: "workflow_definitions",
                columns: new[] { "OrganizationId", "DefinitionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_department_members_DepartmentId_ParticipantId",
                schema: "Workflow",
                table: "workflow_department_members",
                columns: new[] { "DepartmentId", "ParticipantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_department_members_ParticipantId",
                schema: "Workflow",
                table: "workflow_department_members",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_departments_IsActive",
                schema: "Workflow",
                table: "workflow_departments",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_departments_OrganizationId",
                schema: "Workflow",
                table: "workflow_departments",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_events_OrganizationId_OccurredAt",
                schema: "Workflow",
                table: "workflow_events",
                columns: new[] { "OrganizationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_events_WorkflowInstanceId",
                schema: "Workflow",
                table: "workflow_events",
                column: "WorkflowInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_execution_tokens_OrganizationId",
                schema: "Workflow",
                table: "workflow_execution_tokens",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_execution_tokens_WorkflowInstanceId_BranchKey",
                schema: "Workflow",
                table: "workflow_execution_tokens",
                columns: new[] { "WorkflowInstanceId", "BranchKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_execution_tokens_WorkflowInstanceId_Status",
                schema: "Workflow",
                table: "workflow_execution_tokens",
                columns: new[] { "WorkflowInstanceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_group_members_AssignmentGroupId_ParticipantId",
                schema: "Workflow",
                table: "workflow_group_members",
                columns: new[] { "AssignmentGroupId", "ParticipantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_group_members_ParticipantId",
                schema: "Workflow",
                table: "workflow_group_members",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_incidents_OrganizationId",
                schema: "Workflow",
                table: "workflow_incidents",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_incidents_Status_Severity",
                schema: "Workflow",
                table: "workflow_incidents",
                columns: new[] { "Status", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_incidents_WorkflowInstanceId_Status",
                schema: "Workflow",
                table: "workflow_incidents",
                columns: new[] { "WorkflowInstanceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_instances_OrganizationId",
                schema: "Workflow",
                table: "workflow_instances",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_instances_OrganizationId_IdempotencyKey",
                schema: "Workflow",
                table: "workflow_instances",
                columns: new[] { "OrganizationId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_instances_ParentInstanceId",
                schema: "Workflow",
                table: "workflow_instances",
                column: "ParentInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_instances_Status",
                schema: "Workflow",
                table: "workflow_instances",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_instances_WorkflowBindingId",
                schema: "Workflow",
                table: "workflow_instances",
                column: "WorkflowBindingId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_integration_inbox_IdempotencyKey",
                schema: "Workflow",
                table: "workflow_integration_inbox",
                column: "IdempotencyKey");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_integration_inbox_MessageId",
                schema: "Workflow",
                table: "workflow_integration_inbox",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_integration_inbox_OrganizationId",
                schema: "Workflow",
                table: "workflow_integration_inbox",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_integration_inbox_Status_CreatedAt",
                schema: "Workflow",
                table: "workflow_integration_inbox",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_integration_outbox_CorrelationId",
                schema: "Workflow",
                table: "workflow_integration_outbox",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_integration_outbox_MessageId",
                schema: "Workflow",
                table: "workflow_integration_outbox",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_integration_outbox_OrganizationId",
                schema: "Workflow",
                table: "workflow_integration_outbox",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_integration_outbox_Status_CreatedAt",
                schema: "Workflow",
                table: "workflow_integration_outbox",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_notification_logs_CreatedAt",
                schema: "Workflow",
                table: "workflow_notification_logs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_notification_logs_OrganizationId",
                schema: "Workflow",
                table: "workflow_notification_logs",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_notification_logs_TemplateKey",
                schema: "Workflow",
                table: "workflow_notification_logs",
                column: "TemplateKey");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_participants_IsActive",
                schema: "Workflow",
                table: "workflow_participants",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_participants_OrganizationId",
                schema: "Workflow",
                table: "workflow_participants",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_participants_OrganizationId_UserId",
                schema: "Workflow",
                table: "workflow_participants",
                columns: new[] { "OrganizationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_requests_CurrentTaskDueAtUtc",
                schema: "Workflow",
                table: "workflow_requests",
                column: "CurrentTaskDueAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_requests_OrganizationId_CurrentAssignedGroupId_Status",
                schema: "Workflow",
                table: "workflow_requests",
                columns: new[] { "OrganizationId", "CurrentAssignedGroupId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_requests_OrganizationId_RequestNumber",
                schema: "Workflow",
                table: "workflow_requests",
                columns: new[] { "OrganizationId", "RequestNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_requests_OrganizationId_Status",
                schema: "Workflow",
                table: "workflow_requests",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_requests_OrganizationId_WorkflowInstanceId",
                schema: "Workflow",
                table: "workflow_requests",
                columns: new[] { "OrganizationId", "WorkflowInstanceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_timers_OrganizationId_Status_DueAt",
                schema: "Workflow",
                table: "workflow_timers",
                columns: new[] { "OrganizationId", "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_timers_Status_DueAt",
                schema: "Workflow",
                table: "workflow_timers",
                columns: new[] { "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_timers_WorkflowInstanceId",
                schema: "Workflow",
                table: "workflow_timers",
                column: "WorkflowInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_transitions_FromActivityDefinitionId",
                schema: "Workflow",
                table: "workflow_transitions",
                column: "FromActivityDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_transitions_ToActivityDefinitionId",
                schema: "Workflow",
                table: "workflow_transitions",
                column: "ToActivityDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_transitions_WorkflowVersionId_TransitionKey",
                schema: "Workflow",
                table: "workflow_transitions",
                columns: new[] { "WorkflowVersionId", "TransitionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_variable_definitions_WorkflowVersionId_VariableKey",
                schema: "Workflow",
                table: "workflow_variable_definitions",
                columns: new[] { "WorkflowVersionId", "VariableKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_variables_OrganizationId",
                schema: "Workflow",
                table: "workflow_variables",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_variables_WorkflowInstanceId_VariableName",
                schema: "Workflow",
                table: "workflow_variables",
                columns: new[] { "WorkflowInstanceId", "VariableName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_versions_WorkflowDefinitionId",
                schema: "Workflow",
                table: "workflow_versions",
                column: "WorkflowDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_versions_WorkflowDefinitionId_VersionNumber",
                schema: "Workflow",
                table: "workflow_versions",
                columns: new[] { "WorkflowDefinitionId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_instances",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "business_calendar_holidays",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "business_calendar_periods",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "sla_policies",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "Tenants",
                schema: "Tenancy");

            migrationBuilder.DropTable(
                name: "transition_instances",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "work_item_candidates",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "work_items",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_activity_action_definitions",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_activity_assignment_rules",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_activity_outcome_definitions",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_binding_assignment_mappings",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_bindings",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_definitions",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_department_members",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_events",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_execution_tokens",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_group_members",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_incidents",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_instances",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_integration_inbox",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_integration_outbox",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_notification_logs",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_requests",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_timers",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_transitions",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_variable_definitions",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_variables",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "business_calendars",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_activity_definitions",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_departments",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_assignment_groups",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_participants",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_versions",
                schema: "Workflow");
        }
    }
}
