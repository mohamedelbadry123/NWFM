using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MappingStatusEligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "StatusEnteredAt",
                schema: "Workflow",
                table: "workflow_instances",
                type: "datetime2",
                nullable: true);

            // Only recorded transition evidence can establish historical eligibility. UpdatedAt
            // also changes on graph progress, so it must never stand in for a status timestamp.
            migrationBuilder.Sql("""
                UPDATE i SET [StatusEnteredAt] = CASE
                    WHEN i.[Status] = N'Completed' THEN i.[CompletedAt]
                    WHEN i.[Status] = N'Cancelled' THEN i.[CancelledAt]
                    WHEN i.[Status] = N'Suspended' THEN i.[SuspendedAt]
                    WHEN i.[Status] = N'Running' AND e.[EventType] IN (N'InstanceStarted', N'InstanceResumed') AND e.[OccurredAt] = i.[UpdatedAt] THEN e.[OccurredAt]
                    WHEN i.[Status] = N'Failed' AND e.[EventType] = N'InstanceFailed' AND e.[OccurredAt] = i.[UpdatedAt] THEN e.[OccurredAt]
                    ELSE NULL END
                FROM [Workflow].[workflow_instances] i
                OUTER APPLY (
                    SELECT TOP (1) [EventType], [OccurredAt]
                    FROM [Workflow].[workflow_events]
                    WHERE [WorkflowInstanceId] = i.[Id] AND [OrganizationId] = i.[OrganizationId]
                        AND [EventType] IN (N'InstanceStarted', N'InstanceResumed', N'InstanceFailed',
                            N'InstanceCompleted', N'InstanceCancelled', N'InstanceSuspended')
                    ORDER BY [OccurredAt] DESC, [Id] ASC
                ) e;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StatusEnteredAt",
                schema: "Workflow",
                table: "workflow_instances");
        }
    }
}
