# API response language

All application-generated API response messages must be in English across this repository, including Response.Message, Title, DevMessage, ProblemDetails titles/details, validation errors and exceptions intentionally exposed to clients. Keep new and modified messages in English. Do not translate user-provided content or business data such as names, addresses or descriptions. Preserve the existing service ownership and architecture instructions in src/Services/AGENTS.md.

Validation errors must also expose a customer-safe English top-level `message` field, while preserving field-level `errors` and HTTP 400. Register the shared AddValidationMessages MVC extension in service APIs; do not send raw parser exceptions or database errors to customers.
