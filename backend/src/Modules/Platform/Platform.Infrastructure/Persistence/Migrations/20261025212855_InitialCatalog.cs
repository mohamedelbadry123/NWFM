using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "DatabaseServers",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EncryptedAdminConnectionString = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    UseContainedUsers = table.Column<bool>(type: "bit", nullable: false),
                    MaxDatabases = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseServers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FriendlyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Xml = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Plans",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DescriptionEn = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MaxUsers = table.Column<int>(type: "int", nullable: true),
                    MaxTeams = table.Column<int>(type: "int", nullable: true),
                    MaxStorageGb = table.Column<int>(type: "int", nullable: true),
                    TrialDays = table.Column<int>(type: "int", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformAuditEntries",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TargetType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TargetId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformUsers",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    FailedLoginCount = table.Column<int>(type: "int", nullable: false),
                    LockoutEndUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLoginAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SignupRequests",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OrganizationName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AdminFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AdminEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AdminPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PlanCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    DatabaseMode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ByoServer = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: true),
                    ByoPort = table.Column<int>(type: "int", nullable: true),
                    ByoDatabase = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ByoUserName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    EncryptedByoPassword = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ByoRuntimeUserName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    EncryptedByoRuntimePassword = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ByoTrustServerCertificate = table.Column<bool>(type: "bit", nullable: false),
                    EmailVerificationTokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EmailVerificationExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EmailVerificationAttempts = table.Column<int>(type: "int", nullable: false),
                    EmailVerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ApprovalRequired = table.Column<bool>(type: "bit", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LastCompletedStep = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ProvisioningAttempts = table.Column<int>(type: "int", nullable: false),
                    ClientIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignupRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StatusReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ContactName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContactEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContactPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SeedProfile = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StatusChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SsoEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SsoEntityId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    SsoIdentityProviderEntityId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    SsoMetadataUrl = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DefaultLanguage = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    ListedPublicly = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SignupProvisioningEvents",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SignupRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Step = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignupProvisioningEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignupProvisioningEvents_SignupRequests_SignupRequestId",
                        column: x => x.SignupRequestId,
                        principalSchema: "catalog",
                        principalTable: "SignupRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TrialEndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentPeriodEndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Plans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "catalog",
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "catalog",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupportAccessGrants",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    GrantedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    GrantedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportAccessGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportAccessGrants_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "catalog",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantDatabases",
                schema: "catalog",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DatabaseServerId = table.Column<int>(type: "int", nullable: true),
                    DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LoginName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RuntimePrincipal = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EncryptedConnectionString = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    EncryptedAdminConnectionString = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ConnectionStringName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SchemaVersion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LastMigratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastMigrationError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantDatabases", x => x.TenantId);
                    table.ForeignKey(
                        name: "FK_TenantDatabases_DatabaseServers_DatabaseServerId",
                        column: x => x.DatabaseServerId,
                        principalSchema: "catalog",
                        principalTable: "DatabaseServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenantDatabases_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "catalog",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantHosts",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Host = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantHosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantHosts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "catalog",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupportSessions",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlatformUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlatformUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TicketReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ImpersonatedUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ImpersonatedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HandoffCodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    HandoffExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HandoffRedeemedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportSessions_PlatformUsers_PlatformUserId",
                        column: x => x.PlatformUserId,
                        principalSchema: "catalog",
                        principalTable: "PlatformUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportSessions_SupportAccessGrants_GrantId",
                        column: x => x.GrantId,
                        principalSchema: "catalog",
                        principalTable: "SupportAccessGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseServers_Name",
                schema: "catalog",
                table: "DatabaseServers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Plans_Code",
                schema: "catalog",
                table: "Plans",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAuditEntries_AtUtc",
                schema: "catalog",
                table: "PlatformAuditEntries",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAuditEntries_TenantId",
                schema: "catalog",
                table: "PlatformAuditEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformUsers_NormalizedEmail",
                schema: "catalog",
                table: "PlatformUsers",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignupProvisioningEvents_SignupRequestId",
                schema: "catalog",
                table: "SignupProvisioningEvents",
                column: "SignupRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_SignupRequests_AdminEmail",
                schema: "catalog",
                table: "SignupRequests",
                column: "AdminEmail");

            migrationBuilder.CreateIndex(
                name: "IX_SignupRequests_Status",
                schema: "catalog",
                table: "SignupRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SignupRequests_TenantKey",
                schema: "catalog",
                table: "SignupRequests",
                column: "TenantKey");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PlanId",
                schema: "catalog",
                table: "Subscriptions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_TenantId",
                schema: "catalog",
                table: "Subscriptions",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportAccessGrants_TenantId_ExpiresAtUtc",
                schema: "catalog",
                table: "SupportAccessGrants",
                columns: new[] { "TenantId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportSessions_GrantId",
                schema: "catalog",
                table: "SupportSessions",
                column: "GrantId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportSessions_HandoffCodeHash",
                schema: "catalog",
                table: "SupportSessions",
                column: "HandoffCodeHash",
                unique: true,
                filter: "[HandoffCodeHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupportSessions_PlatformUserId",
                schema: "catalog",
                table: "SupportSessions",
                column: "PlatformUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportSessions_TenantId",
                schema: "catalog",
                table: "SupportSessions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantDatabases_DatabaseServerId",
                schema: "catalog",
                table: "TenantDatabases",
                column: "DatabaseServerId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantHosts_Host",
                schema: "catalog",
                table: "TenantHosts",
                column: "Host",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantHosts_TenantId",
                schema: "catalog",
                table: "TenantHosts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Key",
                schema: "catalog",
                table: "Tenants",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProtectionKeys",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "PlatformAuditEntries",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "SignupProvisioningEvents",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Subscriptions",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "SupportSessions",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "TenantDatabases",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "TenantHosts",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "SignupRequests",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Plans",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "PlatformUsers",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "SupportAccessGrants",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "DatabaseServers",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Tenants",
                schema: "catalog");
        }
    }
}
