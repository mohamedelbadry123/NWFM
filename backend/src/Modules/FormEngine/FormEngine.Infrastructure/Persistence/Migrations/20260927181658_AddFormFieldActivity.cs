using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FormEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormFieldActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FieldActivityCode",
                schema: "FormEngine",
                table: "FormDefinitions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormDefinitions_DepartmentCode_FieldActivityCode",
                schema: "FormEngine",
                table: "FormDefinitions",
                columns: new[] { "DepartmentCode", "FieldActivityCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FormDefinitions_DepartmentCode_FieldActivityCode",
                schema: "FormEngine",
                table: "FormDefinitions");

            migrationBuilder.DropColumn(
                name: "FieldActivityCode",
                schema: "FormEngine",
                table: "FormDefinitions");
        }
    }
}
