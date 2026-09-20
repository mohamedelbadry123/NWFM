using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FieldActivityTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_LKP_DEPARTMENT_Code",
                schema: "Auth",
                table: "LKP_DEPARTMENT",
                column: "Code");

            migrationBuilder.CreateTable(
                name: "LKP_FIELD_ACTIVITY_TYPE",
                schema: "Auth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DepartmentCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LKP_FIELD_ACTIVITY_TYPE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LKP_FIELD_ACTIVITY_TYPE_LKP_DEPARTMENT_DepartmentCode",
                        column: x => x.DepartmentCode,
                        principalSchema: "Auth",
                        principalTable: "LKP_DEPARTMENT",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LKP_FIELD_ACTIVITY_TYPE_DepartmentCode_Code",
                schema: "Auth",
                table: "LKP_FIELD_ACTIVITY_TYPE",
                columns: new[] { "DepartmentCode", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LKP_FIELD_ACTIVITY_TYPE",
                schema: "Auth");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_LKP_DEPARTMENT_Code",
                schema: "Auth",
                table: "LKP_DEPARTMENT");
        }
    }
}
