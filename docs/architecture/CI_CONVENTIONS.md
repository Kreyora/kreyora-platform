# CI Conventions

## Scope

M02-S06 provides automatic pull-request validation and separately dispatched Docker validation. It does not publish images, deploy an environment, create releases, or contact a commerce provider.

## Branch convention

The `Pull request CI` workflow validates every pull request. The `Manual Docker validation` workflow runs only when a maintainer starts it from the GitHub Actions page. Feature work should use short-lived descriptive branches and merge through review into the repository owner's selected protected integration branch. Branch naming and protection configuration remain repository-administration decisions; the workflows do not hard-code a deployment branch.

## Required checks

- Backend restore, formatting, Release build, and unit, architecture, contract, and integration tests.
- Frontend frozen install, lint, TypeScript type check, Vitest suite, and production build.
- Gitleaks secret scanning on pull requests, using the repository-managed `GITLEAKS_LICENSE` and GitHub-provided token. Findings block the pull request.

The following M02-S06 validations are intentionally disabled from automatic CI for now: controlled PostgreSQL migration validation, OpenAPI generated-file drift validation, and Dependency Review. The existing `.github/dependency-review-config.yml` is retained for a future re-enable; no unsupported security job is silently treated as passing.

Manual Docker validation has two independently visible jobs: one API image build and one web image build. Neither image is published.

## Caching and reproducibility

The frontend uses pnpm `11.13.0` consistently in the root `packageManager` field, GitHub Actions setup, and the web Dockerfile. The GitHub Actions pnpm cache is keyed by `pnpm-lock.yaml`, while installation always uses `pnpm install --frozen-lockfile`. NuGet caching is intentionally deferred until committed NuGet lock files exist, so cache restoration cannot hide dependency drift.

## Artifacts

M02-S06 does not publish release artifacts, Docker images, packages, or generated deployments. Failed backend test results are uploaded for 14 days to aid diagnosis; successful runs rely on workflow logs.

## Local parity

Run the commands documented in the M02-S06 checkpoint before requesting review. Migration and OpenAPI drift commands remain documented for local or future CI use, but are not automatic PR checks at this time.
