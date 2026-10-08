#!/usr/bin/env node
// M09-S08 evaluation runner (ADR-023). One command per run; resumable across days (free-tier quota).
//   - a throwaway Postgres container `kreyora-eval-pg` (removed at the end unless --keep);
//   - migrate + development seed (owner@kreyora.test) + the API on :5032 with the assistant on and evaluation budgets;
//   - the harness seeds the synthetic shop (fake-catalog.v1.json) and runs dataset v2 through the real assistant via
//     the owner playground (writes dry-run, nothing sent); results append to artifacts/evaluations/M09-S08/runs/.
//
// Usage:
//   node scripts/eval/run-pipeline.mjs              # live Gemini (synthetic data only, ADR-018); full set
//   node scripts/eval/run-pipeline.mjs --screening  # the 26 screening cases
//   node scripts/eval/run-pipeline.mjs --fake       # offline dry run (Fake AI), results in a temp folder
// Needs user secrets (set by you, never printed): Development:Seed:DemoPassword; for live runs Ai:Providers:GoogleAiStudio:ApiKey.
import { randomBytes } from "node:crypto";
import { spawn, spawnSync } from "node:child_process";
import { createWriteStream, mkdirSync, mkdtempSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const apiProject = path.join(repoDir, "services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj");
const harnessProject = path.join(repoDir, "services/api/tools/Kreyora.AiEvaluation/Kreyora.AiEvaluation.csproj");
const CONTAINER = "kreyora-eval-pg";
const PG_PORT = 55460;
const API_URL = "http://localhost:5032";
const args = new Set(process.argv.slice(2));
const fake = args.has("--fake");
const set = args.has("--screening") ? "screening" : "full";
const interval = valueOf("--interval") ?? (fake ? "0.2" : "12"); // ~5 requests/min at 3 calls per case: under free per-minute limits
const logDir = mkdtempSync(path.join(os.tmpdir(), "kreyora-eval-"));
const outDir = fake ? path.join(logDir, "results") : null;
let api = null;

function valueOf(flag) {
  const list = process.argv.slice(2);
  const i = list.indexOf(flag);
  return i >= 0 ? list[i + 1] : undefined;
}

function log(line) {
  console.log(`[eval] ${line}`);
}

function run(command, commandArgs, env, inherit = false) {
  const result = spawnSync(command, commandArgs, { env, stdio: inherit ? "inherit" : ["ignore", "ignore", "inherit"] });
  if (result.status !== 0) throw new Error(`${command} ${commandArgs.slice(0, 3).join(" ")} failed (exit ${result.status})`);
}

async function waitFor(url, label, timeoutMs = 120_000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      if ((await fetch(url)).status < 500) return;
    } catch { /* not up yet */ }
    await new Promise((r) => setTimeout(r, 1000));
  }
  throw new Error(`${label} did not become ready at ${url}`);
}

async function cleanup() {
  if (api && api.exitCode === null) {
    try { process.kill(-api.pid, "SIGTERM"); } catch { /* gone */ }
    await new Promise((r) => setTimeout(r, 1500));
  }
  if (!args.has("--keep")) {
    spawnSync("docker", ["rm", "-f", "-v", CONTAINER], { stdio: "ignore" });
    log(`removed ${CONTAINER}`);
  }
}

process.on("SIGINT", async () => { await cleanup(); process.exit(130); });

try {
  const pgPassword = randomBytes(16).toString("hex"); // throwaway database; never stored
  if (spawnSync("docker", ["inspect", CONTAINER], { stdio: "ignore" }).status === 0) spawnSync("docker", ["rm", "-f", "-v", CONTAINER], { stdio: "ignore" });
  log(`starting ${CONTAINER} on 127.0.0.1:${PG_PORT} (throwaway)`);
  run("docker", ["run", "-d", "--name", CONTAINER, "--label", "kreyora.eval=true", "-e", `POSTGRES_PASSWORD=${pgPassword}`, "-e", "POSTGRES_DB=kreyora_eval",
    "-p", `127.0.0.1:${PG_PORT}:5432`, "postgres:16-alpine"]);
  for (let i = 0; i < 60 && spawnSync("docker", ["exec", CONTAINER, "pg_isready", "-U", "postgres", "-d", "kreyora_eval"], { stdio: "ignore" }).status !== 0; i++) {
    await new Promise((r) => setTimeout(r, 1000));
  }

  const env = {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: "Development",
    ASPNETCORE_URLS: API_URL,
    Database__ConnectionString: `Host=127.0.0.1;Port=${PG_PORT};Database=kreyora_eval;Username=postgres;Password=${pgPassword}`,
    Email__Smtp__ApplicationName: "Kreyora Eval",
    Email__Smtp__Host: "localhost",
    Email__Smtp__Port: "1025",
    Email__Smtp__Security: "None",
    Email__Smtp__SenderEmail: "no-reply@kreyora.test",
    Email__Smtp__SenderDisplayName: "Kreyora Eval",
    Email__Smtp__ApplicationPublicUrl: "http://localhost:3000",
    BackgroundJobs__ServerEnabled: "false",
    Ai__Enabled: "true",
    Ai__Mode: fake ? "Fake" : "Live",
    // Evaluation budgets: the playground still runs every per-turn limit (calls, tools, tokens, deadline).
    Ai__Orchestration__MaxTurnsPerTenantPerDay: "100000",
    Ai__Orchestration__MaxModelCallsPerDay: "100000",
  };
  mkdirSync(logDir, { recursive: true });
  log(`migrating and seeding (logs: ${logDir})`);
  run("dotnet", ["run", "--project", apiProject, "--no-launch-profile", "--", "--migrate"], env);
  run("dotnet", ["run", "--project", apiProject, "--no-launch-profile", "--no-build", "--", "--seed"], env);

  const out = createWriteStream(path.join(logDir, "api.log"));
  api = spawn("dotnet", ["run", "--project", apiProject, "--no-launch-profile", "--no-build"], { env, detached: true, stdio: ["ignore", "pipe", "pipe"] });
  api.stdout.pipe(out);
  api.stderr.pipe(out);
  await waitFor(`${API_URL}/openapi/v1.json`, "API");
  log(`API ready (${fake ? "Fake AI, offline" : "live Gemini, synthetic data"}); running the ${set} set`);

  const harness = ["run", "--project", harnessProject, "--", "pipeline", "--api", API_URL, "--set", set, "--interval", interval];
  if (outDir) harness.push("--out", outDir);
  run("dotnet", harness, process.env, true);
  const report = ["run", "--project", harnessProject, "--no-build", "--", "pipeline-report"];
  if (outDir) report.push("--out", outDir);
  run("dotnet", report, process.env, true);
} catch (error) {
  console.error(`[eval] stopped: ${error instanceof Error ? error.message : String(error)} (API log: ${path.join(logDir, "api.log")})`);
  process.exitCode = 1;
} finally {
  await cleanup();
}
