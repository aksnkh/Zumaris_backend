# Identity integration

Identity now owns independent App, Tech and Admin accounts, each in a separate table. Register/login/refresh/logout are application-specific. See [independent accounts](independent-accounts.md), [authentication boundaries](application-authentication.md) and [administrator creation](administrator-accounts.md).

The user authorized dropping old account data; Identity reset script 008 replaces scripts 001..007 without migration. Only disposable localhost schemas are used by integration tests. The application uses parameterized MySQL SQL; the legacy identity_users table and registration/user-lookup procedures are no longer used. Personal profiles live in their own account table; a read-only directory view supports self-profile/session reads.

Tech registration provisions a separate professional profile in Provider via a short-lived purpose-bound service JWT. It does not use an App token or cross-schema SQL. Deploy Identity and Provider together for this change. Existing JWT keys/issuer/audience configuration and global CORS origins remain required.
