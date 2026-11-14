# Maintenance database

Apply the new 001 migration manually to MySQL 8.0.16+. It creates the service-owned schema and tables without changing other services.
The existing application account needs SELECT/INSERT/UPDATE on its own schema; grants and credentials are not embedded.
IF NOT EXISTS supports repeat execution of unchanged definitions, not schema repair.
See [deployment and API contracts](../../docs/frontend-passport-handoff.md).
