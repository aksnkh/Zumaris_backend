# YARP Gateway

Imported from `gateway.rar` into `src/Gateways/YarpGateway` and the corresponding solution folders. The project name remains `YarpGateway`. Personal Visual Studio files and generated build artifacts are excluded.

## Configuration

- .NET 8; YARP 2.3.0.
- Development HTTP profile: port 5039, Swagger UI at the root path.
- `/identity/{**catch-all}` forwards to Identity after removing `/identity`.
- Development overrides the Identity destination to port 5105, matching its existing launch profile.
- Base configuration retains the supplied Identity and Service Request destination ports 5001 and 5002.
- The supplied `/service-request` route and Swagger entry remain configured, but that service has not been supplied or tested.
- The supplied certificate-validation bypasses were removed. HTTPS destinations use normal certificate validation; current destinations use HTTP.

## Validation

The solution builds successfully; existing nullable initialization warnings remain in the shared response library. Gateway and Identity were run simultaneously with their `http` profiles. Verified through Gateway:

- Root Swagger UI: HTTP 200.
- Identity Swagger JSON: HTTP 200 with the authentication routes.
- Refresh without a cookie: HTTP 401.
- Logout without a bearer token: HTTP 401.
- Malformed login JSON: HTTP 400.

No database commands were executed. Database-backed login, registration, refresh rotation, and the Service Request destination remain untested.

## Existing integration limitations

Identity's Development Swagger document has no `/identity` server prefix, so Swagger **Try it out** through Gateway needs a subsequent OpenAPI server/path adjustment. Proxy endpoints themselves work with the `/identity` prefix.

Identity sets its refresh cookie path to `/api/auth`; browsers calling `/identity/api/auth` will need cookie-path adaptation for the complete login/refresh flow. This is additional integration work, alongside the Identity refresh-record mapping and password logging issues documented in `identity.md`.
