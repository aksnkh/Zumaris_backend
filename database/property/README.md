# Property database

Manually execute `001_properties.sql` on MySQL 8.0.16+. It creates `zumaris_property` and the properties, property_memberships, property_user_preferences and property_membership_history tables. No other service's schema is modified.

The application account needs SELECT, INSERT and UPDATE on this schema. Existing table definitions are not migrated by IF NOT EXISTS. Property deletion is soft archive, preserving records and declaration history.

No migration runs automatically. See `docs/property.md` for deployment and optional isolated MySQL tests.
