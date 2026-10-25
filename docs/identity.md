# Identity integration

The supplied `identity.rar` is integrated into the root solution under `src/Services/Identity`, with shared response types under `src/BuildingBlocks`. Generated artifacts, personal Visual Studio files, the archive's separate solution and deployment workflow were excluded.

The configuration dependencies target stable .NET 8 packages. API, Application, Domain and Infrastructure remain separate projects, and persistence uses MySqlConnector with direct parameterized SQL and the existing registration/user-lookup stored procedures; no ORM is used.

JWT/session behavior has since been revised. See [JWT setup, client contract and validation](jwt.md). Run the supplied [manual MySQL migration](../database/identity/001_auth_sessions.sql) before using login/session features in this version. The migration was not executed during development.

The previous password logging and missing refresh user mapping have been removed with the session redesign. Existing connection strings, signing values and password-hash format are unchanged. Nullable initialization warnings in the supplied shared response library remain unrelated to this change.


راهنمای ورود Google و تنظیمات ارتباط سرور Next.js: [google-login.md](google-login.md).

## MySQL session regression test

`MySqlSessionTests` covers GUID materialization, session lookup/listing, refresh rotation and reuse revocation with the real connector. Set `ZUMARIS_IDENTITY_MYSQL_TEST_CONNECTION` only to a disposable loopback MySQL 8 database named `zumaris_identity_test`, then run `dotnet test tests/Identity.Tests/Identity.Tests.csproj`. The test applies `database/identity/001_auth_sessions.sql` only to that isolated schema and cleans up its session. Without the variable this one test is explicitly skipped; never point it at the application database.

Session and external-login identifiers are read through `GetGuid` to support MySqlConnector's automatic CHAR(36)-to-Guid conversion. Using `GetString` for these columns can throw InvalidCastException on production refresh.
