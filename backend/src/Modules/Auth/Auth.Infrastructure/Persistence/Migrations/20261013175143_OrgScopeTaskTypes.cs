using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrgScopeTaskTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Coverage is narrowed by task type now, and activity type codes name no task type, so
            // their narrowing goes. A row that named nothing but activity types would be left naming
            // nothing at all, which a scope may not, so it goes with them.
            migrationBuilder.Sql("""
                DELETE s FROM [Auth].[OrgScopes] s
                WHERE s.Level IS NULL
                  AND NOT EXISTS (SELECT 1 FROM [Auth].[OrgScopesDepartment] d WHERE d.OrgScopeId = s.Id)
                  AND EXISTS (SELECT 1 FROM [Auth].[OrgScopesActivityType] a WHERE a.OrgScopeId = s.Id);
                """);

            migrationBuilder.DropTable(
                name: "OrgScopesActivityType",
                schema: "Auth");

            migrationBuilder.CreateTable(
                name: "OrgScopesTaskTypes",
                schema: "Auth",
                columns: table => new
                {
                    OrgScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgScopesTaskTypes", x => new { x.OrgScopeId, x.TaskTypeId });
                    table.ForeignKey(
                        name: "FK_OrgScopesTaskTypes_OrgScopes_OrgScopeId",
                        column: x => x.OrgScopeId,
                        principalSchema: "Auth",
                        principalTable: "OrgScopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Task types have no activity type to go back to and are dropped; the activity type table
            // comes back empty.
            migrationBuilder.DropTable(
                name: "OrgScopesTaskTypes",
                schema: "Auth");

            migrationBuilder.CreateTable(
                name: "OrgScopesActivityType",
                schema: "Auth",
                columns: table => new
                {
                    OrgScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityTypeCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgScopesActivityType", x => new { x.OrgScopeId, x.ActivityTypeCode });
                    table.ForeignKey(
                        name: "FK_OrgScopesActivityType_OrgScopes_OrgScopeId",
                        column: x => x.OrgScopeId,
                        principalSchema: "Auth",
                        principalTable: "OrgScopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }
    }
}
