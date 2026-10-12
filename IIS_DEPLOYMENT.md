# IIS key storage

## Startup database diagnostics

The normal publish-copy/configure/restart workflow is supported. The app connects to
SQL Server and applies pending migrations before starting HTTP service. A pre-login
SSL handshake timeout at this step prevents the site from starting; it is not evidence
of a missing schema migration or encrypted extract column.

Diagnostic build `uat-startup-diagnostics-v1` logs the actual hosting environment,
runtime content root, winning connection configuration provider, SQL server/database,
authentication mode and TLS/timeout options before migration startup. It never logs
the complete connection string, user ID or password. Compare these entries with the
intended UAT configuration. Environment-specific appsettings files override the base
file, and environment variables override both. Paths in exception source locations
come from build symbols; `contentRoot` identifies the running deployment location.

The connection preflight now checks EF's database-existence connection (master) as
well as the application database, with bounded timeout retries before schema writes.
Retries do not repair a persistent UAT endpoint/TLS/connectivity failure.

Copy the fresh Release publish folder to the server using the normal deployment
procedure, retaining the correct server settings and persistent ASP.NET key store.
`deployment-manifest.json` identifies the supplied DLL hash. After recycling, the
startup log must include `ReconTool startup uat-startup-diagnostics-v1` and
`SQL configuration:`. Those lines establish which configuration the process actually
uses. Do not infer UAT connectivity from a successful local run.

ReconTool persists ASP.NET authentication and antiforgery keys instead of relying on
IIS profile/registry discovery. New stores have private Windows ACLs and keys are
encrypted with DPAPI. These keys protect browser cookies and antiforgery tokens.
Card matching uses plain card numbers and has no key or cryptography dependency.
Do not delete the ASP.NET key store to resolve a browser error.

For IIS, provision a private directory outside the website publish directory, for example
`C:\ProgramData\ReconTool\ApplicationKeys`. Grant Modify only to the application's
actual app-pool identity; retain administrator and SYSTEM access. Do not grant access
to all users or all IIS applications. Configure the deployed appsettings.Production.json:

```json
{
  "DataProtection": {
    "KeyDirectory": "C:\\ProgramData\\ReconTool\\ApplicationKeys",
    "ProtectToLocalMachine": true
  }
}
```

The equivalent environment variables are `DataProtection__KeyDirectory` and
`DataProtection__ProtectToLocalMachine`. Without an override, the app uses
LocalApplicationData/ReconTool/ApplicationKeys when available, otherwise
CommonApplicationData/ReconTool/ApplicationKeys. Failure to access the persistent
store must be corrected through directory permissions; do not switch to ephemeral keys.

Deploy the updated application and recycle its app pool. Clear this site's cookies
and sign in again once after moving from ephemeral keys. Previously issued tokens
cannot be recovered if their keys were lost. Keep the persistent store across future
deployments/recycles. Machine DPAPI is for a single Windows server; a multi-server
deployment requires a separately configured shared protected key store.

The startup message `No migrations were applied. The database is already up to date`
means the schema migrations succeeded previously. EF relationship/default warnings
are separate from key storage. A duplicate `IX_ImportBatches_SourceType_ContentHash`
error is a separate import-state/concurrency issue: inspect the existing import's
status before retrying, rather than deleting reconciliation data or encryption keys.
