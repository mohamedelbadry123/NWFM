using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrgScopeDepartmentsActivityTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateTable(
                name: "OrgScopesDepartment",
                schema: "Auth",
                columns: table => new
                {
                    OrgScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgScopesDepartment", x => new { x.OrgScopeId, x.DepartmentCode });
                    table.ForeignKey(
                        name: "FK_OrgScopesDepartment_OrgScopes_OrgScopeId",
                        column: x => x.OrgScopeId,
                        principalSchema: "Auth",
                        principalTable: "OrgScopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Each scope's one department moves to the new table before its column goes.
            migrationBuilder.Sql("""
                INSERT INTO [Auth].[OrgScopesDepartment] (OrgScopeId, DepartmentCode)
                SELECT Id, LTRIM(RTRIM(DepartmentId))
                FROM [Auth].[OrgScopes]
                WHERE DepartmentId IS NOT NULL AND LTRIM(RTRIM(DepartmentId)) <> N'';
                """);

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                schema: "Auth",
                table: "OrgScopes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DepartmentId",
                schema: "Auth",
                table: "OrgScopes",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            // The column holds one department: a scope that named several keeps the first of them.
            // Activity types have nowhere to go back to and are dropped.
            migrationBuilder.Sql("""
                UPDATE s SET DepartmentId = d.DepartmentCode
                FROM [Auth].[OrgScopes] s
                CROSS APPLY (SELECT MIN(DepartmentCode) AS DepartmentCode
                             FROM [Auth].[OrgScopesDepartment] WHERE OrgScopeId = s.Id) d
                WHERE d.DepartmentCode IS NOT NULL;
                """);

            migrationBuilder.DropTable(
                name: "OrgScopesActivityType",
                schema: "Auth");

            migrationBuilder.DropTable(
                name: "OrgScopesDepartment",
                schema: "Auth");
        }
    }
}
