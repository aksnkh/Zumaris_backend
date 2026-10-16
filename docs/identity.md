# Identity integration

The supplied `identity.rar` is integrated into the root solution under `src/Services/Identity`, with shared response types under `src/BuildingBlocks`. Generated artifacts, personal Visual Studio files, the archive's separate solution and deployment workflow were excluded.

The configuration dependencies target stable .NET 8 packages. API, Application, Domain and Infrastructure remain separate projects, and persistence uses MySqlConnector with direct parameterized SQL and the existing registration/user-lookup stored procedures; no ORM is used.

JWT/session behavior has since been revised. See [JWT setup, client contract and validation](jwt.md). Run the supplied [manual MySQL migration](../database/identity/001_auth_sessions.sql) before using login/session features in this version. The migration was not executed during development.

The previous password logging and missing refresh user mapping have been removed with the session redesign. Existing connection strings, signing values and password-hash format are unchanged. Nullable initialization warnings in the supplied shared response library remain unrelated to this change.
