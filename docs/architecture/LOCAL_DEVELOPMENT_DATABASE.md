# Local Development PostgreSQL

## Security boundary

Never commit a database password, connection string containing a password, `.env` file, or local `appsettings` file. Secret scanning is a safety net, not a reason to store credentials in Git.

The Web API uses .NET User Secrets during the Development environment. The project records only a non-secret User Secrets identifier. Each developer stores their own connection string in their OS user profile, outside the repository.

Password-reset SMTP configuration follows the same boundary; see [Local Email Delivery and Password Reset](LOCAL_EMAIL_DELIVERY.md).

## Standard local database

Use one persistent Docker container per development machine:

- Container: `kreyora-dev-db`
- Image: `postgres:16-alpine`
- Host port: `55432`
- Database: `kreyora_dev`
- User: `kreyora_dev`
- Docker volume: `kreyora_dev_pgdata`

The password is selected and retained by the developer. It must not be copied into this document, source-controlled configuration, or GitHub Actions workflow YAML.

## First-time setup

1. Install and start Docker Desktop.
2. Create the named volume and database container. Replace `<your-local-password>` before running:

   ```powershell
   docker run -d --name kreyora-dev-db --restart unless-stopped `
     -e POSTGRES_DB=kreyora_dev `
     -e POSTGRES_USER=kreyora_dev `
     -e POSTGRES_PASSWORD=<your-local-password> `
     -p 55432:5432 `
     -v kreyora_dev_pgdata:/var/lib/postgresql/data `
     --health-cmd "pg_isready -U kreyora_dev -d kreyora_dev" `
     --health-interval 5s --health-timeout 5s --health-retries 12 `
     postgres:16-alpine
   ```

3. From `services/api`, save the machine-local connection string with .NET User Secrets:

   ```powershell
   dotnet user-secrets set "Database:ConnectionString" "Host=localhost;Port=55432;Database=kreyora_dev;Username=kreyora_dev;Password=<your-local-password>" --project src/Kreyora.WebApi/Kreyora.WebApi.csproj
   ```

4. Apply migrations:

   ```powershell
   dotnet run --project src/Kreyora.WebApi/Kreyora.WebApi.csproj -- --migrate
   ```

5. Connect a visual database client such as DBeaver or pgAdmin to `localhost:55432`, database `kreyora_dev`, user `kreyora_dev`, and the locally chosen password.

## Daily commands

```powershell
docker start kreyora-dev-db
docker stop kreyora-dev-db
docker logs kreyora-dev-db
```

The named Docker volume keeps data when the container is stopped. To intentionally discard all local development data, stop and remove the container, then remove `kreyora_dev_pgdata`.

## CI and shared environments

- Local development: .NET User Secrets or environment variables.
- GitHub Actions: GitHub Actions Secrets for any real credential; use service containers with job-scoped credentials where possible.
- Committed files: examples and placeholders only, such as `.env.example` or `appsettings.Development.json.example`.

Do not bypass Gitleaks or add allowlists for real credentials. Rotate any password accidentally committed, even if a scan later removes the finding.
