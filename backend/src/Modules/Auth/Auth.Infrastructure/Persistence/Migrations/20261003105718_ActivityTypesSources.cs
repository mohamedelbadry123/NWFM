using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ActivityTypesSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LKP_FIELD_ACTIVITY_TYPE_LKP_DEPARTMENT_DepartmentCode",
                schema: "Auth",
                table: "LKP_FIELD_ACTIVITY_TYPE");

            migrationBuilder.DropIndex(
                name: "IX_LKP_FIELD_ACTIVITY_TYPE_DepartmentCode_Code",
                schema: "Auth",
                table: "LKP_FIELD_ACTIVITY_TYPE");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_LKP_DEPARTMENT_Code",
                schema: "Auth",
                table: "LKP_DEPARTMENT");

            migrationBuilder.DropColumn(
                name: "DepartmentCode",
                schema: "Auth",
                table: "LKP_FIELD_ACTIVITY_TYPE");

            migrationBuilder.CreateTable(
                name: "LKP_ACTIVITY_SOURCE",
                schema: "Auth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LKP_ACTIVITY_SOURCE", x => x.Id);
                    table.UniqueConstraint("AK_LKP_ACTIVITY_SOURCE_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "LKP_FIELD_ACTIVITY_TYPE_SOURCE",
                schema: "Auth",
                columns: table => new
                {
                    FieldActivityTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LKP_FIELD_ACTIVITY_TYPE_SOURCE", x => new { x.FieldActivityTypeId, x.SourceCode });
                    table.ForeignKey(
                        name: "FK_LKP_FIELD_ACTIVITY_TYPE_SOURCE_LKP_ACTIVITY_SOURCE_SourceCode",
                        column: x => x.SourceCode,
                        principalSchema: "Auth",
                        principalTable: "LKP_ACTIVITY_SOURCE",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LKP_FIELD_ACTIVITY_TYPE_SOURCE_LKP_FIELD_ACTIVITY_TYPE_FieldActivityTypeId",
                        column: x => x.FieldActivityTypeId,
                        principalSchema: "Auth",
                        principalTable: "LKP_FIELD_ACTIVITY_TYPE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // A code was unique only within its department; now it is unique outright. Forms, workflows
            // and SLA policies refer to an activity by code alone, so the oldest row of each code stays.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT ROW_NUMBER() OVER (PARTITION BY Code ORDER BY CreatedAt, Id) AS rn
                    FROM [Auth].[LKP_FIELD_ACTIVITY_TYPE])
                DELETE FROM ranked WHERE rn > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_LKP_FIELD_ACTIVITY_TYPE_Code",
                schema: "Auth",
                table: "LKP_FIELD_ACTIVITY_TYPE",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LKP_ACTIVITY_SOURCE_Code",
                schema: "Auth",
                table: "LKP_ACTIVITY_SOURCE",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LKP_FIELD_ACTIVITY_TYPE_SOURCE_SourceCode",
                schema: "Auth",
                table: "LKP_FIELD_ACTIVITY_TYPE_SOURCE",
                column: "SourceCode");

            // Every activity type needs at least one source: the existing ones are NWFM's own.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Auth].[LKP_ACTIVITY_SOURCE] WHERE Code = N'NWFM')
                    INSERT INTO [Auth].[LKP_ACTIVITY_SOURCE] (Id, Code, NameEn, NameAr, Kind, Url, IsActive, CreatedAt, UpdatedAt)
                    VALUES (NEWID(), N'NWFM', N'NWFM', N'NWFM', N'Internal', NULL, 1, SYSUTCDATETIME(), SYSUTCDATETIME());

                INSERT INTO [Auth].[LKP_FIELD_ACTIVITY_TYPE_SOURCE] (FieldActivityTypeId, SourceCode)
                SELECT Id, N'NWFM' FROM [Auth].[LKP_FIELD_ACTIVITY_TYPE];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LKP_FIELD_ACTIVITY_TYPE_SOURCE",
                schema: "Auth");

            migrationBuilder.DropTable(
                name: "LKP_ACTIVITY_SOURCE",
                schema: "Auth");

            migrationBuilder.DropIndex(
                name: "IX_LKP_FIELD_ACTIVITY_TYPE_Code",
                schema: "Auth",
                table: "LKP_FIELD_ACTIVITY_TYPE");

            migrationBuilder.AddColumn<string>(
                name: "DepartmentCode",
                schema: "Auth",
                table: "LKP_FIELD_ACTIVITY_TYPE",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            // The department each type had is gone; file them all under the first one so the key holds.
            migrationBuilder.Sql("""
                UPDATE [Auth].[LKP_FIELD_ACTIVITY_TYPE]
                SET DepartmentCode = (SELECT TOP 1 Code FROM [Auth].[LKP_DEPARTMENT] ORDER BY Code);
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_LKP_DEPARTMENT_Code",
                schema: "Auth",
                table: "LKP_DEPARTMENT",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_LKP_FIELD_ACTIVITY_TYPE_DepartmentCode_Code",
                schema: "Auth",
                table: "LKP_FIELD_ACTIVITY_TYPE",
                columns: new[] { "DepartmentCode", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LKP_FIELD_ACTIVITY_TYPE_LKP_DEPARTMENT_DepartmentCode",
                schema: "Auth",
                table: "LKP_FIELD_ACTIVITY_TYPE",
                column: "DepartmentCode",
                principalSchema: "Auth",
                principalTable: "LKP_DEPARTMENT",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
