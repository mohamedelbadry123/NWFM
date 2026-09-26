using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FormEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FormTablePerForm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The one table every form used to share. It was created by native SQL, not by a
            // migration, so EF does not know to drop it. Each form now writes to its own table,
            // rebuilt at startup from the published versions; nothing reads this one any more.
            migrationBuilder.Sql("DROP TABLE IF EXISTS [FE].[Submissions];");

            migrationBuilder.DropTable(
                name: "FieldCatalog",
                schema: "FE");

            migrationBuilder.AddColumn<string>(
                name: "SubmissionTable",
                schema: "FE",
                table: "FormDefinitions",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FormFields",
                schema: "FE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DataName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FieldType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LabelEn = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LabelAr = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsCompanion = table.Column<bool>(type: "bit", nullable: false),
                    FirstVersionNo = table.Column<int>(type: "int", nullable: false),
                    LastVersionNo = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormFields_FormDefinitions_FormDefinitionId",
                        column: x => x.FormDefinitionId,
                        principalSchema: "FE",
                        principalTable: "FormDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormDefinitions_SubmissionTable",
                schema: "FE",
                table: "FormDefinitions",
                column: "SubmissionTable",
                unique: true,
                filter: "[SubmissionTable] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FormFields_DataName",
                schema: "FE",
                table: "FormFields",
                column: "DataName");

            migrationBuilder.CreateIndex(
                name: "IX_FormFields_FormDefinitionId_DataName",
                schema: "FE",
                table: "FormFields",
                columns: new[] { "FormDefinitionId", "DataName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormFields",
                schema: "FE");

            migrationBuilder.DropIndex(
                name: "IX_FormDefinitions_SubmissionTable",
                schema: "FE",
                table: "FormDefinitions");

            migrationBuilder.DropColumn(
                name: "SubmissionTable",
                schema: "FE",
                table: "FormDefinitions");

            migrationBuilder.CreateTable(
                name: "FieldCatalog",
                schema: "FE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FieldType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LabelAr = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LabelEn = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldCatalog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FieldCatalog_DataName",
                schema: "FE",
                table: "FieldCatalog",
                column: "DataName",
                unique: true);
        }
    }
}
