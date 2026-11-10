# Authentication: independent App, Tech and Admin accounts

The shared-account/multiple-membership model is superseded. Identity now owns separate `app_accounts`, `tech_accounts` and `admin_accounts` tables. Each has its own email/username uniqueness, ID, credentials and personal profile. Matching email never merges or upgrades another account.

See [the complete independent-account API and destructive reset guide](independent-accounts.md).

| Application | Register | Login | Refresh | Logout |
|---|---|---|---|---|
| App | `/identity/api/app/register` | `/identity/api/app/login` | `/identity/api/app/refresh` | `/identity/api/app/logout` |
| Tech | `/identity/api/tech/register` | `/identity/api/tech/login` | `/identity/api/tech/refresh` | `/identity/api/tech/logout` |
| Admin | `/identity/api/admin/register` | `/identity/api/admin/login` | `/identity/api/admin/refresh` | `/identity/api/admin/logout` |

All are POST. Login takes `{ "userName": "person@example.com", "password": "..." }`; the route selects the application and extra fields are rejected. CSRF headers remain mandatory for Identity writes. Cookies stay HttpOnly and separate per application. Register App/Tech returns `Response<AuthResponse>`; Admin registration returns created account information without replacing the creator's session. Tech register requires explicit Provider terms consent and automatically provisions its own Provider profile through a purpose-bound server credential. Retry Tech login completes provisioning after a transient Provider outage.

Admin registration requires an active Admin account/token or a private server key for the one-time first administrator. Normal public registration never creates an administrator. See [administrator accounts](administrator-accounts.md).

JWT claims are `sub`, `sid`, `application`, `role` and Tech-only `providerId`. The user record must belong to the issuing application's table. Refresh and session validation retain that same account/table. Google subject links are scoped by application and never implicitly linked by email. Tech/Admin Google login requires a previously registered, explicitly linked account.

Every deployed API service retains application isolation and resource ownership:

- App: properties, customer service requests/history, customer quote list/accept/decline and payments.
- Tech: Provider own profile/documents, technician opportunities/jobs and quote context/submit/sent/withdraw.
- Admin: administrator operations plus current active Admin-account validation.
- Explicitly shared: catalog, public business summaries/reviews, quote details and job-photo access with role-specific ownership.
- Shared self profile/sessions: only the current account ID and application.

The prior App-to-Tech Provider enrolment exception is removed. An App token cannot register a Provider. Same email in another application grants no permissions. `/api/auth/signup` and legacy register are App-only aliases; the old login route is retained only as an explicit-application alias that reads the separate table.

The user authorized a fresh reset instead of data migration. Run Identity reset 008 and the separate Provider data-reset script as documented, then deploy Identity and Provider. Identity scripts 001..007 are historical and must not be applied over the replacement schema. No user database SQL is executed by application startup.
