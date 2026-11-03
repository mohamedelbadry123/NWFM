-- The containerized control plane's owner login on the host's SQL Server. Containers cannot use
-- Windows authentication, so the platform reaches the catalog and tenant databases with this login.
-- It creates databases and logins for new workspaces, so it is a sysadmin: local development only.
-- Variables are validated by start-docker.ps1 before sqlcmd substitution.
-- This script never resets the password of an existing login.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF SUSER_ID(N'$(NWFM_SQL_OWNER_USER)') IS NULL
    CREATE LOGIN [$(NWFM_SQL_OWNER_USER)] WITH PASSWORD=N'$(NWFM_SQL_OWNER_PASSWORD)',
        CHECK_POLICY=ON, DEFAULT_DATABASE=[master];
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.sql_logins
    WHERE name=N'$(NWFM_SQL_OWNER_USER)' AND PWDCOMPARE(N'$(NWFM_SQL_OWNER_PASSWORD)',password_hash)=1)
    THROW 51320, 'The existing Docker owner login has another password. Rotate it explicitly; it will not be replaced.', 1;

IF EXISTS (SELECT 1 FROM sys.sql_logins WHERE name=N'$(NWFM_SQL_OWNER_USER)' AND is_disabled=1)
    THROW 51321, 'The Docker owner login is disabled. Enable it explicitly; it will not be re-enabled.', 1;

IF IS_SRVROLEMEMBER(N'sysadmin', N'$(NWFM_SQL_OWNER_USER)') <> 1
    ALTER SERVER ROLE [sysadmin] ADD MEMBER [$(NWFM_SQL_OWNER_USER)];

PRINT N'Docker owner login ready.';
