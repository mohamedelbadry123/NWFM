-- The tenant API's catalog login. It reads the tenant directory and the shared Data Protection key
-- ring, and writes only a workspace's own support-access consent and the audit rows that record it.
-- Tenant connection strings stay encrypted; only the control plane holds the owner credentials.
-- Variables are validated by provision-local-runtime.ps1 before sqlcmd substitution.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_ID(N'NWFM_Catalog') IS NULL
    THROW 51310, 'Start the control plane (NWFM.Platform.Api) first: it creates and migrates the catalog.', 1;

IF SUSER_ID(N'nwfm_catalog_reader') IS NULL
    CREATE LOGIN [nwfm_catalog_reader] WITH PASSWORD=N'$(NWFM_CATALOG_READER_PASSWORD)',
        CHECK_POLICY=ON, DEFAULT_DATABASE=[NWFM_Catalog];
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.sql_logins
    WHERE name=N'nwfm_catalog_reader' AND PWDCOMPARE(N'$(NWFM_CATALOG_READER_PASSWORD)',password_hash)=1)
    THROW 51311, 'The existing catalog reader login has another password. Rotate it explicitly; it will not be replaced.', 1;
GO

USE [NWFM_Catalog];
GO

IF USER_ID(N'nwfm_catalog_reader') IS NULL
    CREATE USER [nwfm_catalog_reader] FOR LOGIN [nwfm_catalog_reader];

GRANT CONNECT TO [nwfm_catalog_reader];
-- The tenant directory and the shared key ring. Not readable: platform staff, database servers,
-- signup requests (they hold customer passwords) and any tenant's owner connection string.
GRANT SELECT ON OBJECT::[catalog].[Tenants] TO [nwfm_catalog_reader];
GRANT SELECT ON OBJECT::[catalog].[TenantHosts] TO [nwfm_catalog_reader];
GRANT SELECT ON OBJECT::[catalog].[Subscriptions] TO [nwfm_catalog_reader];
GRANT SELECT ON OBJECT::[catalog].[Plans] TO [nwfm_catalog_reader];
GRANT SELECT ON OBJECT::[catalog].[DataProtectionKeys] TO [nwfm_catalog_reader];
GRANT SELECT ON OBJECT::[catalog].[TenantDatabases]
    ([TenantId], [Mode], [EncryptedConnectionString], [ConnectionStringName], [RuntimePrincipal],
     [VerifiedAtUtc], [SchemaVersion]) TO [nwfm_catalog_reader];
GRANT SELECT ON OBJECT::[catalog].[SupportAccessGrants] TO [nwfm_catalog_reader];
GRANT SELECT ON OBJECT::[catalog].[SupportSessions] TO [nwfm_catalog_reader];
-- A workspace administrator grants and revokes support access from the tenant app; redeeming a
-- handoff code and ending a session update the session row; each is audited.
GRANT INSERT, UPDATE ON OBJECT::[catalog].[SupportAccessGrants] TO [nwfm_catalog_reader];
GRANT UPDATE ON OBJECT::[catalog].[SupportSessions] TO [nwfm_catalog_reader];
GRANT INSERT ON OBJECT::[catalog].[PlatformAuditEntries] TO [nwfm_catalog_reader];
GO

PRINT N'Catalog reader provisioned for the tenant API.';
