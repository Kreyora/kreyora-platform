# ADR-002: Cookie Browser Authentication

- **Status:** Accepted
- **Date:** 2026-08-01
- **Owner:** Project owner

## Decision

Kreyora uses ASP.NET Core Identity password authentication with an encrypted HttpOnly cookie for the seller browser. Production cookies use `Secure`, `SameSite=Lax`, `__Host-` names, and an eight-hour fixed expiry. State-changing auth requests require an antiforgery header; CORS permits credentials only for configured exact origins.

Self-registration creates a user, tenant, and Owner membership atomically. Social login and invitations are deferred. Password reset is a Development-only token-return flow until an approved email provider exists; production responses remain generic and do not claim delivery.

## Consequences

The browser does not store bearer tokens. Future OIDC can validate an external identity and then establish the same Kreyora browser session. Future microservices receive a short-lived service credential or trusted context rather than the browser cookie.
