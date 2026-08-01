# ADR-004: SMTP Password Reset Delivery

- **Status:** Accepted
- **Date:** 2026-08-02
- **Owner:** Project owner

## Context

ADR-002 originally allowed a Development-only password-reset token response and browser-visible reset link while no mail provider existed. The project owner superseded that reset-delivery decision: password reset now requires real SMTP delivery before later roadmap work continues.

## Decision

Kreyora uses the existing ASP.NET Core Identity password-reset token provider and sends the resulting reset URL through an Application-layer `IEmailSender` abstraction. Infrastructure provides `SmtpEmailSender` using MailKit. Controllers and browser handlers contain no SMTP-specific logic.

The reset request endpoint always returns the same generic `202 Accepted` message. Tokens and reset URLs are never returned by the API, displayed in Development, logged, or attached to exceptions. Identity token expiry is configured through `Email:Smtp:PasswordResetTokenLifetimeMinutes`; normal Identity validation rejects invalid, expired, and reused tokens.

SMTP is configured by typed settings. Development may use the local Mailpit test receiver or controlled Gmail App Password credentials stored in .NET User Secrets. Production receives credentials from a secure environment/secret store and requires HTTPS reset URLs plus TLS SMTP. No SMTP credential belongs in source control.

## Consequences

- The Next.js recovery screen always instructs the user to check email and has no local-development continuation link.
- Mail delivery remains replaceable: SES, Postmark, Mailgun, SendGrid, or another SMTP provider can replace Gmail without changing the authentication contract.
- Mail delivery failure is intentionally not exposed to the requester, preserving account-enumeration resistance. Operations must monitor the generic failure event safely.
- This decision supersedes only the Development-only reset token/link paragraph in ADR-002; the cookie, CSRF, and browser-session decisions in ADR-002 remain accepted.
