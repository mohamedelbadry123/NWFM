using Microsoft.EntityFrameworkCore.Migrations;

namespace Workflow.Infrastructure.Persistence.Migrations;

public sealed partial class ActivityFormProgress : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(name: "FormProgressJson", schema: "Workflow", table: "work_items", type: "nvarchar(max)", nullable: true);
    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "FormProgressJson", schema: "Workflow", table: "work_items");
}
