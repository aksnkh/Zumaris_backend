# PropertyIntelligence database

Manually apply `001_health_scores.sql` on MySQL 8.0.16+. It creates the service-owned
assessment, assessment-history and calculation tables without modifying other schemas.
Grant the actual runtime account SELECT/INSERT/UPDATE separately. No migration runs on startup.
See [Health Score deployment](../../docs/health-score.md).
