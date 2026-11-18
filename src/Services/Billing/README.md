# Billing

Authenticated, customer-owned fixed-price payments for completed ServiceRequest jobs.

## Presentation mode

`Payments:DemoMode` is enabled in the shipped appsettings for the current presentation.
POST `/billing/api/payments` with `{ "requestId": "..." }` confirms immediately without PayPal:
`status: Paid`, `gateway: Demo`, `isDemo: true`, `canPay: false`, `checkoutUrl: null`.
GET `/billing/api/payments/{id}` returns the persisted result. Repeated POSTs return the same
payment ID; existing AwaitingGateway intents are promoted on POST when demo mode is enabled.
Ownership, authentication, completed-job and authoritative fixed-quote validation still apply.
This does not transfer money or change the ServiceRequest execution status from Completed.
The separate ServiceRequest history payment_status/Paid filter is not synchronized by this API.

Existing databases: manually apply `database/billing/002_demo_payments.sql` before publishing
Billing.Api. New databases use 001. No user database is modified automatically.
Disable with `Payments__DemoMode=false` or `Payments:DemoMode=false` in private local settings
before real use. Disabling does not relabel historical Demo records as real payments.
With demo disabled new intents remain AwaitingGateway; real checkout/webhooks/settlement are deferred.
