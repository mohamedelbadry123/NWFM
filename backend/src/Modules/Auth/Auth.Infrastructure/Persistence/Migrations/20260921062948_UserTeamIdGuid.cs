using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auth.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// <c>Users.TeamId</c> pointed at <c>Auth.Teams.Id</c>, a GUID, from a BIGINT column — so no value it
    /// held could ever match a team. SQL Server cannot convert BIGINT to UNIQUEIDENTIFIER in place, and
    /// there is nothing worth converting, so the column is dropped and re-added. Team logins are made
    /// by the teams screen from here on.
    /// </summary>
    public partial class UserTeamIdGuid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TeamId",
                schema: "Auth",
                table: "Users");

            migrationBuilder.AddColumn<Guid>(
                name: "TeamId",
                schema: "Auth",
                table: "Users",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TeamId",
                schema: "Auth",
                table: "Users");

            migrationBuilder.AddColumn<long>(
                name: "TeamId",
                schema: "Auth",
                table: "Users",
                type: "bigint",
                nullable: true);
        }
    }
}
