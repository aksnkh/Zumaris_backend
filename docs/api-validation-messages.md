# Customer-facing validation messages

All service APIs register the shared `AddValidationMessages()` MVC extension. Validation responses retain HTTP 400, the existing ProblemDetails fields and the field-level `errors` object, and additionally expose top-level `message` and `statusCode`. Frontends can display `response.message` directly. This does not translate user data or change success responses.

Domain validation messages are joined with spaces, with duplicates removed. Automatic model-binding validation (missing required fields, malformed JSON, wrong types) uses safe English messages instead of raw JSON parser/exception output. Trace IDs remain available for diagnostics; they are not customer instructions. API-authentication and server errors are outside this validation-specific change.

Example:

```json
{
  "status": 400,
  "statusCode": 400,
  "title": "One or more validation errors occurred.",
  "message": "Provide the date, start and end together. Only Emergency may omit all three.",
  "errors": {
    "PreferredDate": [
      "Provide the date, start and end together. Only Emergency may omit all three."
    ]
  }
}
```

Publish each deployed service whose validation behavior needs updating, including its shared dependency. There is no database or Gateway configuration change. Future service APIs should register the extension when adding MVC controllers. Covered tests include ServiceRequest scheduling and history validation, malformed JSON, Identity required inputs and Property pagination.
