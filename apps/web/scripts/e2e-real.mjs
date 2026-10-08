#!/usr/bin/env node
// M08-S06 real-backend end-to-end harness (opt-in; not part of ci:frontend).
// Starts a THROWAWAY Postgres container, migrates and seeds it (Development-only E2E personas and a
// Simulator connection with run-scoped random secrets), runs the API (Hangfire on) and Next against it,
// executes the Playwright `e2e-real` suite, then removes the container and its anonymous volume.
// Project containers, images, volumes and data are never touched.
import { spawn, spawnSync } from "node:child_process";
import { randomBytes } from "node:crypto";
import { mkdtempSync, rmSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const webDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const repoDir = path.resolve(webDir, "../..");
const apiProject = path.join(repoDir, "services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj");

const runId = randomBytes(4).toString("hex");
const container = `kreyora-e2e-${runId}`;
const pgPort = Number(process.env.E2E_PG_PORT ?? 55440);
const apiUrl = process.env.E2E_API_URL ?? "http://localhost:5030";
const baseUrl = process.env.E2E_BASE_URL ?? "http://localhost:3100";
const pgPassword = randomBytes(12).toString("hex");
const demoPassword = `Aa1!${randomBytes(12).toString("base64url")}`;
const simulatorSecret = randomBytes(24).toString("hex");
// Session cookies saved by the suite's global setup; deleted on cleanup.
const authDir = mkdtempSync(path.join(os.tmpdir(), "kreyora-e2e-auth-"));

const apiEnv = {
  ...process.env,
  ASPNETCORE_ENVIRONMENT: "Development",
  ASPNETCORE_URLS: apiUrl,
  Database__ConnectionString: `Host=127.0.0.1;Port=${pgPort};Database=kreyora_e2e;Username=postgres;Password=${pgPassword}`,
  SecretEncryption__MasterKey: randomBytes(32).toString("base64"),
  Cors__AllowedOrigins__0: baseUrl,
  Email__Smtp__ApplicationName: "Kreyora E2E",
  Email__Smtp__Host: "localhost",
  Email__Smtp__Port: "1025",
  Email__Smtp__Security: "None",
  Email__Smtp__SenderEmail: "no-reply@kreyora.test",
  Email__Smtp__SenderDisplayName: "Kreyora E2E",
  Email__Smtp__ApplicationPublicUrl: baseUrl,
  Development__Seed__DemoPassword: demoPassword,
  Development__Seed__E2ePersonas: "true",
  Development__Seed__SimulatorSecret: simulatorSecret,
  // M09-S08: assistant screens run against the real API with the Fake model (no provider is ever called).
  Ai__Enabled: "true",
  Ai__Mode: "Fake",
};

const children = [];

function run(command, args, options = {}) {
  const result = spawnSync(command, args, { stdio: "inherit", ...options });
  if (result.status !== 0) throw new Error(`${command} ${args.join(" ")} failed with exit code ${result.status}`);
}

function start(name, command, args, options) {
  const child = spawn(command, args, { stdio: ["ignore", "pipe", "pipe"], detached: true, ...options });
  child.stdout.on("data", (d) => process.env.E2E_VERBOSE && process.stdout.write(`[${name}] ${d}`));
  child.stderr.on("data", (d) => process.stderr.write(`[${name}] ${d}`));
  children.push(child);
  return child;
}

async function waitFor(url, label, timeoutMs = 120_000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      const response = await fetch(url);
      if (response.status < 500) return;
    } catch {
      /* not up yet */
    }
    await new Promise((r) => setTimeout(r, 1000));
  }
  throw new Error(`${label} did not become ready at ${url}`);
}

function cleanup() {
  for (const child of children) {
    try { process.kill(-child.pid, "SIGTERM"); } catch { /* already exited */ }
  }
  // -v removes the container's anonymous data volume; nothing else is touched.
  rmSync(authDir, { recursive: true, force: true });
  spawnSync("docker", ["rm", "-f", "-v", container], { stdio: "ignore" });
  console.log(`[e2e] removed throwaway container ${container} and its anonymous volume`);
}

process.on("SIGINT", () => { cleanup(); process.exit(130); });

let exitCode = 1;
try {
  console.log(`[e2e] starting throwaway Postgres ${container} on 127.0.0.1:${pgPort}`);
  run("docker", ["run", "-d", "--name", container, "--label", "kreyora.e2e=true", "-e", `POSTGRES_PASSWORD=${pgPassword}`,
    "-e", "POSTGRES_DB=kreyora_e2e", "-p", `127.0.0.1:${pgPort}:5432`, "postgres:16-alpine"], { stdio: "ignore" });
  for (let i = 0; i < 60; i++) {
    if (spawnSync("docker", ["exec", container, "pg_isready", "-U", "postgres"], { stdio: "ignore" }).status === 0) break;
    await new Promise((r) => setTimeout(r, 1000));
  }

  console.log("[e2e] migrating and seeding (Development, E2E personas)");
  run("dotnet", ["run", "--project", apiProject, "-c", "Release", "--no-build", "--no-launch-profile", "--", "--migrate"], { env: apiEnv });
  run("dotnet", ["run", "--project", apiProject, "-c", "Release", "--no-build", "--no-launch-profile", "--", "--seed"], { env: apiEnv });

  console.log("[e2e] starting API and web");
  start("api", "dotnet", ["run", "--project", apiProject, "-c", "Release", "--no-build", "--no-launch-profile"], { env: apiEnv });
  await waitFor(`${apiUrl}/openapi/v1.json`, "API");
  const port = new URL(baseUrl).port;
  start("web", "pnpm", ["exec", "next", "dev", "--hostname", "localhost", "--port", port], {
    cwd: webDir,
    env: { ...process.env, NEXT_PUBLIC_API_URL: apiUrl },
  });
  await waitFor(baseUrl, "Web", 180_000);

  // Async on purpose: a synchronous spawn would block this event loop, the API/web log pipes would stop
  // draining, and the API would hang on its next log write once the pipe buffer filled.
  exitCode = await new Promise((resolve) => {
    const playwright = spawn("pnpm", ["exec", "playwright", "test", "-c", "playwright.real.config.ts", ...process.argv.slice(2)], {
      cwd: webDir,
      stdio: "inherit",
      env: { ...process.env, E2E_API_URL: apiUrl, E2E_BASE_URL: baseUrl, E2E_PASSWORD: demoPassword, E2E_SIMULATOR_SECRET: simulatorSecret, E2E_AUTH_DIR: authDir },
    });
    playwright.on("exit", (code) => resolve(code ?? 1));
  });
} catch (error) {
  console.error(`[e2e] ${error instanceof Error ? error.message : String(error)}`);
} finally {
  cleanup();
}
process.exit(exitCode);
