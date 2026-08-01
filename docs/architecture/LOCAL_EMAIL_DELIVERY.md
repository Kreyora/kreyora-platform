# Local Email Delivery and Password Reset

## Security boundary

Kreyora sends password-reset email through the Application-layer `IEmailSender` abstraction. `SmtpEmailSender` is its current MailKit implementation. The reset token is generated and validated only by ASP.NET Core Identity; Kreyora does not create, persist, return, log, or display a custom reset token.

Never commit SMTP credentials, Gmail account passwords, Gmail App Passwords, populated `.env` files, or local `appsettings` files. Secret scanning is a safety net, not a storage mechanism. SMTP failures are recorded only as a generic failure type; the email address, credentials, token, and reset URL are not logged.

## Development: controlled Mailpit inbox

Mailpit is the default local development SMTP receiver. It accepts real SMTP on port `1025` and exposes only a local inspection UI on port `8025`; it does not deliver to external inboxes.

```powershell
docker run --detach --name kreyora-dev-mailpit --publish 1025:1025 --publish 8025:8025 axllent/mailpit:v1.30.6
```

Open `http://localhost:8025` to inspect captured email. The committed Development settings target this local-only receiver with no credentials and `Security=None`; this is valid only for local testing. Start the API and web app with the real API setting, request a reset, open the captured email, and follow its `/recover/reset` link. The link uses `http://localhost:3000` by default.

```powershell
dotnet run --project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj
corepack pnpm --dir apps/web dev
```

Set `NEXT_PUBLIC_API_URL=http://localhost:5030` in the ignored `apps/web/.env.local` before starting the web app. Do not expose Mailpit beyond the development machine.

## Gmail development testing

Gmail can replace Mailpit only for controlled development testing. Use a Gmail App Password, never the normal account password, and use an account/inbox that the project owner controls. Store the values with .NET User Secrets:

```powershell
dotnet user-secrets set "Email:Smtp:Host" "smtp.gmail.com" --project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj
dotnet user-secrets set "Email:Smtp:Port" "587" --project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj
dotnet user-secrets set "Email:Smtp:Username" "your-email@gmail.com" --project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj
dotnet user-secrets set "Email:Smtp:Password" "your-gmail-app-password" --project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj
dotnet user-secrets set "Email:Smtp:Security" "StartTls" --project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj
dotnet user-secrets set "Email:Smtp:SenderEmail" "your-email@gmail.com" --project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj
dotnet user-secrets set "Email:Smtp:SenderDisplayName" "Kreyora" --project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj
dotnet user-secrets set "Email:Smtp:ApplicationPublicUrl" "http://localhost:3000" --project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj
```

Gmail is a development/testing SMTP provider only. A later deployment can use Amazon SES, Postmark, Mailgun, SendGrid, or another SMTP provider without changing password-reset logic. For provider account and App Password requirements, follow [Google's App Password guidance](https://support.google.com/accounts/answer/185833).

## Hosted environments

Inject the variables in [`services/api/.env.example`](../../services/api/.env.example) through the platform's secure secret store or environment configuration. Production startup rejects a non-HTTPS `ApplicationPublicUrl` and rejects `Security=None`; use a valid public HTTPS URL and STARTTLS or implicit TLS. The `PasswordResetTokenLifetimeMinutes` setting configures Identity's official data-protection token lifespan and the expiry shown in the email.

The default public response to every reset request is exactly:

> If an account exists for that email address, password reset instructions will be sent.

The same `202 Accepted` response is returned whether the account exists or SMTP delivery fails. Delivery failures must be investigated through secure operations telemetry; the API must not disclose internal SMTP details to callers.
