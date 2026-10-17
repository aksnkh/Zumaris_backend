# YARP Gateway

For deployment alongside the existing Identity service on IIS port 5001, follow [the IIS deployment guide](iis-gateway.md). Use the `IISProduction` publish profile and republish Identity to apply trusted local HTTPS forwarding.

The project is under `src/Gateways/YarpGateway` and its own solution folders. It targets .NET 8 with YARP 2.3.0. Personal Visual Studio files and generated artifacts are excluded.

- Gateway HTTP profile: port 5039, Swagger UI at the root.
- `/identity/{**catch-all}` forwards to Identity after removing `/identity`.
- Development uses Identity port 5105, matching its launch profile.
- Base configuration keeps the supplied Identity and Service Request ports 5001/5002.
- `/service-request` and its Swagger entry remain configured; that service has not been supplied or tested.
- HTTPS destinations use normal certificate validation.

Run Identity and Gateway with their `http` profiles, either in separate terminals or with Visual Studio **Multiple startup projects**. The Gateway does not replace Identity's JWT validation.

The Identity Swagger document now resolves its server relative to the document, so **Try it out** uses `/identity` through Gateway and the root directly. Refresh cookies use `Path=/` for both paths. See [JWT instructions](jwt.md) for the CSRF header, CORS allowlists, database migration, session endpoints and client changes.

The original wildcard CORS policy has been replaced with explicit `Cors:AllowedOrigins` configuration. Same-origin Swagger needs no CORS entry. No database commands or deployment workflows run automatically.
