-- Day 25 — grants the API's managed identity access to run inside the database it already has
-- network/auth access to (Entra-ID-only auth, see modules/sql.bicep). Bicep can create the SQL
-- *server* and *database*, but "this app identity is allowed to touch tables inside this
-- database" is a T-SQL/DML concern, not an ARM resource — nothing here is automatable as a
-- Bicep resource, hence a script instead of another module.
--
-- Run this ONCE per environment, against the database itself (not master), authenticated as the
-- server's Entra ID admin (the identity behind aadAdminLogin/aadAdminObjectId in main.bicep):
--
--   sqlcmd -S <sqlServerFqdn>,1433 -d orderfulfillmentdb -G -N -C \
--     -i infra/sql/grant-managed-identity.sql -v ManagedIdentityName="orderfulfillment-api-dev"
--
-- (-G = Entra ID auth, using whichever identity `az login`/az CLI is already signed in as — no
-- password, matching this whole capstone's passwordless design. -v defines a sqlcmd variable so
-- the same script serves every environment; pass the exact containerAppName from that
-- environment's .bicepparam / azd env values.)

DECLARE @managedIdentityName sysname = N'$(ManagedIdentityName)';
DECLARE @sql nvarchar(max);

-- CREATE USER ... FROM EXTERNAL PROVIDER is what actually maps the Container App's Entra ID
-- object (the same principalId modules/api.bicep outputs and servicebus-access.bicep grants
-- Service Bus access to) onto a database-level principal. Without this, the connection string's
-- "Authentication=Active Directory Default" still authenticates successfully at the server level
-- — Entra ID accepted the identity — but every query then fails with "login succeeded, but user
-- does not have permission" at the database level, because no database user exists for it yet.
SET @sql = N'IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @name)
BEGIN
    EXEC(''CREATE USER [' + @managedIdentityName + N'] FROM EXTERNAL PROVIDER'');
END';
EXEC sp_executesql @sql, N'@name sysname', @name = @managedIdentityName;

-- Least privilege: read + write the app's own tables, nothing else (not db_owner, not schema
-- changes). Ordering.Infrastructure's own EF Core migrations/EnsureCreated still need to run as
-- the Entra admin, not this identity — this grant is for the running app, not for deploying it.
--
-- Day 29: assign the concatenation to @sql first, same as the CREATE USER block above, rather
-- than inlining it as EXEC sp_executesql's first argument — confirmed live, EXEC's argument-list
-- grammar doesn't accept a bare "literal + variable + literal" expression there ("Incorrect
-- syntax near '+'"), unlike a general expression context. This script had never actually been
-- run against a live database before today (Day 25 wrote it, Day 25's own gap said so).
SET @sql = N'ALTER ROLE db_datareader ADD MEMBER [' + @managedIdentityName + N']';
EXEC sp_executesql @sql;
SET @sql = N'ALTER ROLE db_datawriter ADD MEMBER [' + @managedIdentityName + N']';
EXEC sp_executesql @sql;

-- Verify: should return one row, with the roles above listed.
SELECT
    dp.name AS principal_name,
    dp.type_desc,
    STRING_AGG(rp.name, ', ') AS role_memberships
FROM sys.database_principals dp
LEFT JOIN sys.database_role_members drm ON drm.member_principal_id = dp.principal_id
LEFT JOIN sys.database_principals rp ON rp.principal_id = drm.role_principal_id
WHERE dp.name = @managedIdentityName
GROUP BY dp.name, dp.type_desc;
