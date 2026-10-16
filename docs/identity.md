# Identity integration

Imported from the supplied `identity.rar` into the existing root solution. Relative project references are retained. Generated `bin`, `obj`, `.vs`, personal `.user` files, the archive's separate solution, and its deployment workflow are not imported. No automatic deployment is configured.

The three `Microsoft.Extensions.Configuration*` dependencies were changed from .NET 11 previews to stable 8.0.0 packages for the .NET 8 target. Application behavior and the supplied connection/JWT configuration are otherwise preserved.

## Verified

- Solution build with .NET SDK 8.0.426.
- API startup using the `http` profile.
- Swagger JSON returns HTTP 200 and describes login, register, refresh, and logout.
- Refresh without a cookie and logout without authorization return HTTP 401.
- Malformed login JSON returns HTTP 400.

The archive contains no test project. Database-backed registration, login, and token rotation have not been validated. Nullable initialization warnings in the supplied shared response class may appear on a clean build.

## Database

No SQL was executed and no database schema changes are required merely to import the projects. The archive contains no schema or stored procedure definitions. Before preparing compatible SQL changes, provide the existing MySQL schema and procedure definitions (without table data), along with the MySQL server version. SQL changes will be supplied for manual execution.

`AuthAdoNetRepository` calls these procedures:

| Procedure | Parameters |
| --- | --- |
| `sp_identity_get_user_by_username` | `p_user_name` |
| `sp_identity_register_user` | `p_user_name`, `p_password_hash`, `p_email` |
| `sp_identity_insert_refresh_token` | `p_id`, `p_user_id`, `p_token_hash`, `p_expires_at`, `p_created_at` |
| `sp_identity_get_refresh_token` | `p_token_hash` |
| `sp_identity_rotate_refresh_token` | `p_old_token_hash`, `p_new_id`, `p_user_id`, `p_new_token_hash`, `p_new_expires_at`, `p_new_created_at`, output `p_is_success` |
| `sp_identity_revoke_refresh_token` | `p_token_hash` |

## Existing behavior to review separately

- `AuthService.LoginAsync` writes passwords and password hashes to console output. Remove this logging before using actual user credentials.
- `GetRefreshTokenAsync` does not populate `UserName`, `Email`, or `UserIsActive`, although refresh validation depends on those properties. The default `UserIsActive = false` causes retrieved refresh records to fail `IsActive`. This existing application defect needs correction and database-backed validation.
