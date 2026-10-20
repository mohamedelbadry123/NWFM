-- Variables are validated by provision-local-runtime.sh before sqlcmd substitution.
-- This script never changes tenant ownership, resets data, or rotates an existing login.
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF DB_NAME()<>N'NWFM' OR NOT EXISTS (
    SELECT 1 FROM Tenancy.DeploymentBoundary
    WHERE SingletonId=1 AND TenantId=CONVERT(uniqueidentifier,N'$(NWFM_TENANT_ID)'))
    THROW 51300, 'Initialize the intended local tenant database before provisioning requests.', 1;

IF SUSER_ID(N'nwfm_request') IS NULL
    CREATE LOGIN [nwfm_request] WITH PASSWORD=N'$(NWFM_RUNTIME_PASSWORD)',
        CHECK_POLICY=ON, DEFAULT_DATABASE=[NWFM];
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.sql_logins
    WHERE name=N'nwfm_request' AND PWDCOMPARE(N'$(NWFM_RUNTIME_PASSWORD)',password_hash)=1)
    THROW 51301, 'The existing request login has another password. Rotate it explicitly; it will not be replaced.', 1;

IF USER_ID(N'nwfm_request') IS NULL
    CREATE USER [nwfm_request] FOR LOGIN [nwfm_request];
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.database_principals WHERE name=N'nwfm_request' AND sid=SUSER_SID(N'nwfm_request'))
    THROW 51302, 'The existing request database user has another owner.', 1;

-- Server inventory visibility uses SQL Server's existing public metadata grant.
-- This local helper never grants server-wide permissions. If an operator removed
-- that visibility, the API refuses startup until isolation can be verified.
GRANT CONNECT TO [nwfm_request];
GRANT VIEW DEFINITION ON USER::[nwfm_form_storage_owner] TO [nwfm_request];
GRANT SELECT ON OBJECT::Tenancy.DeploymentBoundary TO [nwfm_request];
GRANT VIEW DEFINITION ON OBJECT::Tenancy.LifecycleAudit TO [nwfm_request];
GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::Auth TO [nwfm_request];
GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::FormEngine TO [nwfm_request];
GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::Task TO [nwfm_request];
GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::Workflow TO [nwfm_request];
-- Dynamic form publication creates and alters only tables in the FormEngine schema.
GRANT CREATE TABLE TO [nwfm_request];
GRANT ALTER ON SCHEMA::FormEngine TO [nwfm_request];

COMMIT TRANSACTION;
PRINT N'Local tenant request identity provisioned. The API independently verifies its effective permissions at startup.';
