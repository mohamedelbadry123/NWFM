using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MappingDependencyWaits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WaitDependenciesJson",
                schema: "Workflow",
                table: "mapping_invocations",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WaitDependenciesJson",
                schema: "Workflow",
                table: "mapping_invocations");
        }
    }
}
