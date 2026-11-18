# Notification

Owns the internal purpose-bound password-recovery email endpoint and TLS SMTP delivery.
Identity retains the durable encrypted delivery outbox and owns token/account/session rules.
No arbitrary public sender API or public Gateway route is exposed. No new database is needed.
See [password recovery deployment/API](../../../docs/password-recovery.md).

## Gmail / Google Workspace SMTP

The API defaults to `smtp.gmail.com:587` with STARTTLS (`Smtp:EnableTls=true`),
username/from address `info@zumaris.com` and sender name `Zumaris`.
This requires that the mailbox is actually hosted by Google Workspace; a custom-domain
address hosted elsewhere cannot authenticate to Gmail just by setting its address here.

Enable two-step verification on that Google account and create a dedicated app password
if the Workspace administrator permits app passwords. Use the app password, not the normal
Google sign-in password. If the organization's policy prohibits app passwords, use an
administrator-configured SMTP relay or implement OAuth support; this sender does not support OAuth.

Keep `Smtp:Password` empty in source. Set `Smtp__Password` in the Notification service's
server environment (restart/recycle the service afterward), or merge the following into
its private `appsettings.Local.json`, which must remain outside source control:

```json
{
  "Smtp": {
    "Password": "YOUR_GOOGLE_APP_PASSWORD"
  }
}
```

The local file must be in Notification's content root, readable only by its runtime identity
and administrators. Environment variable configuration overrides it. Never put SMTP credentials
in Identity, Gateway, frontend environment variables or browser code.

Allow outbound TCP 587 and restart Notification. `/health` checks process liveness only;
it does not verify SMTP credentials or email delivery. Validate delivery by requesting a reset
for a controlled existing account through `/identity/api/app/forgot-password` (or tech/admin),
with `X-Zumaris-CSRF: 1` and a JSON email body. A 202 response is deliberately generic and is
not evidence of delivery. Verify the inbox/spam folder and complete the reset. Identity recovery
must first be configured, its SQL migration applied and its private Notification URL reachable;
see the deployment guide linked above. Do not expose Notification publicly to test SMTP.
