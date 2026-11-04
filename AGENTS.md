# API response language

All application-generated API response messages must be in English across this repository, including Response.Message, Title, DevMessage, ProblemDetails titles/details, validation errors and exceptions intentionally exposed to clients. Keep new and modified messages in English. Do not translate user-provided content or business data such as names, addresses or descriptions. Preserve the existing service ownership and architecture instructions in src/Services/AGENTS.md.

Validation errors must also expose a customer-safe English top-level `message` field, while preserving field-level `errors` and HTTP 400. Register the shared AddValidationMessages MVC extension in service APIs; do not send raw parser exceptions or database errors to customers.

# Response contract for future work

All newly introduced JSON API responses must use the existing `Zumaris.BuildingBlocks.Common.Responses.Response<T>` model. Put endpoint data in `Payload` and customer-facing English messages in `Message`; use the remaining model fields consistently with the actual HTTP status. Frontends should be able to use one generic response envelope with endpoint-specific payload types.

The user explicitly excluded existing endpoints from a response-format migration. Preserve their current response shapes, and do not add a global wrapper/filter or retrofit old endpoints unless explicitly requested later. New failure responses should follow the shared model where HTTP permits a JSON body; validation must retain field-level errors and an English top-level message. Preserve binary/file responses and HTTP semantics for bodyless statuses such as 204.
