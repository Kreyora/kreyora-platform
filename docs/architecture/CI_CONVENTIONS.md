# CI Conventions

## Scope

M02-S06 provides pull-request and push validation only. It does not publish images, deploy an environment, create releases, or contact a commerce provider.

## Branch convention

CI validates every pull request and every pushed branch. Feature work should use short-lived descriptive branches and merge through review into the repository owner's selected protected integration branch. Branch naming and protection configuration remain repository-administration decisions; the workflow does not hard-code a deployment branch.

## Required checks

- Backend restore, formatting, Release build, and unit, architecture, contract, and integration tests.
- Frontend frozen install, lint, TypeScript type check, Vitest suite, and production build.
- Controlled PostgreSQL migration execution and OpenAPI snapshot/type drift detection.
- API and web Docker image builds without publishing images.
- Gitleaks secret scanning on pushes and pull requests.
- Dependency Review on pull requests, failing newly introduced `high` or `critical` vulnerabilities.

Dependency Review requires a public repository or GitHub Advanced Security for a private repository. Enable the required GitHub capability before treating this check as merge-required; CI does not bypass an unavailable security feature.

## Caching and reproducibility

The frontend uses the GitHub Actions pnpm cache keyed by `pnpm-lock.yaml`, while installation always uses `pnpm install --frozen-lockfile`. NuGet caching is intentionally deferred until committed NuGet lock files exist, so cache restoration cannot hide dependency drift.

## Artifacts

M02-S06 does not publish release artifacts, Docker images, packages, or generated deployments. Failed backend test results and API startup logs are uploaded for 14 days to aid diagnosis; successful runs rely on workflow logs.

## Local parity

Run the commands documented in the M02-S06 checkpoint before requesting review. To verify transport drift, start the Development API with PostgreSQL available and run `pnpm generate:api`; the committed OpenAPI snapshot and generated TypeScript file must remain unchanged.
