using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FormEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormEngineModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "FE");

            migrationBuilder.CreateTable(
                name: "FieldCatalog",
                schema: "FE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DataName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FieldType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LabelEn = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LabelAr = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldCatalog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormDefinitions",
                schema: "FE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DepartmentCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CurrentVersionNo = table.Column<int>(type: "int", nullable: true),
                    SchemaJson = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "{}"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormVersions",
                schema: "FE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    TargetClient = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SchemaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormVersions_FormDefinitions_FormDefinitionId",
                        column: x => x.FormDefinitionId,
                        principalSchema: "FE",
                        principalTable: "FormDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubmissionFiles",
                schema: "FE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormVersionNo = table.Column<int>(type: "int", nullable: true),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContextType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ContextId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DataName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    RelativePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StorageKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "MANAGED"),
                    UploadedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubmissionFiles_FormDefinitions_FormDefinitionId",
                        column: x => x.FormDefinitionId,
                        principalSchema: "FE",
                        principalTable: "FormDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FieldCatalog_DataName",
                schema: "FE",
                table: "FieldCatalog",
                column: "DataName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormDefinitions_Category",
                schema: "FE",
                table: "FormDefinitions",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_FormDefinitions_Code",
                schema: "FE",
                table: "FormDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormDefinitions_DepartmentCode",
                schema: "FE",
                table: "FormDefinitions",
                column: "DepartmentCode");

            migrationBuilder.CreateIndex(
                name: "IX_FormDefinitions_Status",
                schema: "FE",
                table: "FormDefinitions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FormDefinitions_Status_Category",
                schema: "FE",
                table: "FormDefinitions",
                columns: new[] { "Status", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_FormVersions_FormDefinitionId_VersionNo_TargetClient",
                schema: "FE",
                table: "FormVersions",
                columns: new[] { "FormDefinitionId", "VersionNo", "TargetClient" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionFiles_FormDefinitionId",
                schema: "FE",
                table: "SubmissionFiles",
                column: "FormDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionFiles_Status_CreatedAt",
                schema: "FE",
                table: "SubmissionFiles",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionFiles_SubmissionId",
                schema: "FE",
                table: "SubmissionFiles",
                column: "SubmissionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FieldCatalog",
                schema: "FE");

            migrationBuilder.DropTable(
                name: "FormVersions",
                schema: "FE");

            migrationBuilder.DropTable(
                name: "SubmissionFiles",
                schema: "FE");

            migrationBuilder.DropTable(
                name: "FormDefinitions",
                schema: "FE");
        }
    }
}
