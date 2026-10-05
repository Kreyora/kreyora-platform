#!/usr/bin/env node
// M08-S07 sandbox runner. Starts, on this machine only:
//   - Postgres container `kreyora-sandbox-pg` (named volume `kreyora_sandbox_pgdata`, kept between sessions so the
//     next-day window check can reuse it; removed only by `--destroy`);
//   - the API (Development, user secrets) on :5030; the web app on :3000;
//   - the webhook-only proxy on :5031 (the ONLY thing a tunnel may point at);
//   - a control endpoint on 127.0.0.1:5039 (see ctl.mjs); optionally a cloudflared quick tunnel (`--tunnel`).
// The Compose project's containers and data are never touched. API and web logs go to a temp directory, not
// the console, and the log pipes are always drained.
//
//   node scripts/sandbox/run.mjs [--tunnel] [--no-jobs]
//   node scripts/sandbox/run.mjs --destroy [--yes]
import { spawn, spawnSync } from "node:child_process";
import { randomBytes } from "node:crypto";
import { createWriteStream, mkdirSync } from "node:fs";
import http from "node:http";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { prompt } from "./lib.mjs";
import { SANDBOX_CONTAINER, SANDBOX_DATABASE } from "./evidence.mjs";
import { createWebhookProxy } from "./webhook-proxy.mjs";

const repoDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const webDir = path.join(repoDir, "apps/web");
const apiProject = path.join(repoDir, "services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj");
const VOLUME = "kreyora_sandbox_pgdata";
const PG_PORT = 55450;
const API_URL = "http://localhost:5030";
const WEB_URL = "http://localhost:3000";
const PROXY_PORT = 5031;
const CONTROL_PORT = 5039;
const REQUIRED_SECRETS = ["InstagramWebhook:AppSecret", "InstagramWebhook:VerifyToken", "SecretEncryption:MasterKey", "Development:Seed:DemoPassword"];
const PG_SECRET = "Sandbox:PostgresPassword";

const args = new Set(process.argv.slice(2));
const logDir = path.join(os.tmpdir(), "kreyora-sandbox-logs");

if (args.has("--destroy")) {
  await destroy();
  process.exit(0);
}

const children = { api: null, web: null, tunnel: null };
let jobsEnabled = !args.has("--no-jobs");
let shuttingDown = false;

try {
  const secrets = readSecrets();
  // A secret may also come from the environment (Name:Part → Name__Part), which the API honors too.
  const missing = REQUIRED_SECRETS.filter((name) => !secrets.has(name) && !process.env[name.replaceAll(":", "__")]);
  if (missing.length > 0) {
    console.log("Missing user secrets (set them yourself in services/api/src/Kreyora.WebApi):");
    missing.forEach((name) => console.log(`  dotnet user-secrets set "${name}" "<value>"`));
    process.exit(1);
  }
  const pgPassword = secrets.get(PG_SECRET) ?? createPgPassword();
  const apiEnv = {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: "Development",
    ASPNETCORE_URLS: API_URL,
    Database__ConnectionString: `Host=127.0.0.1;Port=${PG_PORT};Database=${SANDBOX_DATABASE};Username=postgres;Password=${pgPassword}`,
    Cors__AllowedOrigins__0: WEB_URL,
    Email__Smtp__ApplicationName: "Kreyora Sandbox",
    Email__Smtp__Host: "localhost",
    Email__Smtp__Port: "1025",
    Email__Smtp__Security: "None",
    Email__Smtp__SenderEmail: "no-reply@kreyora.test",
    Email__Smtp__SenderDisplayName: "Kreyora Sandbox",
    Email__Smtp__ApplicationPublicUrl: WEB_URL,
  };

  mkdirSync(logDir, { recursive: true });
  startDatabase(pgPassword);
  log("migrating and seeding the sandbox database");
  run("dotnet", ["run", "--project", apiProject, "--no-launch-profile", "--", "--migrate"], apiEnv);
  run("dotnet", ["run", "--project", apiProject, "--no-launch-profile", "--no-build", "--", "--seed"], apiEnv);

  await startApi(apiEnv);
  children.web = startLogged("web", "pnpm", ["exec", "next", "dev", "--hostname", "localhost", "--port", "3000"], { cwd: webDir, env: { ...process.env, NEXT_PUBLIC_API_URL: API_URL } });
  await waitFor(WEB_URL, "web", 180_000);

  const proxy = createWebhookProxy({ upstream: API_URL, log });
  await new Promise((resolve) => proxy.server.listen(PROXY_PORT, "127.0.0.1", resolve));
  startControl(proxy, apiEnv);

  log(`ready · inbox ${WEB_URL}/inbox (sign in as owner@kreyora.test) · proxy http://localhost:${PROXY_PORT} · jobs ${jobsEnabled ? "on" : "off"}`);
  log(`logs: ${logDir}`);
  if (args.has("--tunnel")) await startTunnel();
  if (process.stdin.isTTY) listenForKeys(proxy, apiEnv);
} catch (error) {
  console.error(`[sandbox] stopped: ${error instanceof Error ? error.message : String(error)}`);
  await shutdown(1);
}

process.on("SIGINT", () => void shutdown(0));
process.on("SIGTERM", () => void shutdown(0));

function log(line) {
  console.log(line.startsWith("[") ? line : `[sandbox] ${line}`);
}

/** Reads user-secret NAMES for the check; the values stay in this process and are never printed. */
function readSecrets() {
  const result = spawnSync("dotnet", ["user-secrets", "list", "--project", apiProject], { encoding: "utf8" });
  const secrets = new Map();
  for (const line of result.stdout.split("\n")) {
    const index = line.indexOf(" = ");
    if (index > 0) secrets.set(line.slice(0, index).trim(), line.slice(index + 3));
  }
  return secrets;
}

function createPgPassword() {
  const value = randomBytes(18).toString("hex");
  const result = spawnSync("dotnet", ["user-secrets", "set", PG_SECRET, value, "--project", apiProject], { stdio: "ignore" });
  if (result.status !== 0) throw new Error(`could not store ${PG_SECRET} in user secrets`);
  log(`generated a sandbox database password into user secrets (${PG_SECRET})`);
  return value;
}

function run(command, commandArgs, env) {
  const result = spawnSync(command, commandArgs, { env, stdio: ["ignore", "ignore", "inherit"] });
  if (result.status !== 0) throw new Error(`${command} ${commandArgs.slice(-1)[0]} failed (exit ${result.status})`);
}

function startDatabase(pgPassword) {
  const exists = spawnSync("docker", ["inspect", SANDBOX_CONTAINER], { stdio: "ignore" }).status === 0;
  if (exists) {
    log(`starting existing ${SANDBOX_CONTAINER}`);
    spawnSync("docker", ["start", SANDBOX_CONTAINER], { stdio: "ignore" });
  } else {
    log(`creating ${SANDBOX_CONTAINER} with volume ${VOLUME} on 127.0.0.1:${PG_PORT}`);
    const created = spawnSync("docker", ["run", "-d", "--name", SANDBOX_CONTAINER, "--label", "kreyora.sandbox=true",
      "-e", `POSTGRES_PASSWORD=${pgPassword}`, "-e", `POSTGRES_DB=${SANDBOX_DATABASE}`,
      "-v", `${VOLUME}:/var/lib/postgresql/data`, "-p", `127.0.0.1:${PG_PORT}:5432`, "postgres:16-alpine"], { stdio: "ignore" });
    if (created.status !== 0) throw new Error("could not create the sandbox database container");
  }
  for (let i = 0; i < 60; i++) {
    if (spawnSync("docker", ["exec", SANDBOX_CONTAINER, "pg_isready", "-U", "postgres", "-d", SANDBOX_DATABASE], { stdio: "ignore" }).status === 0) return;
    spawnSync("sleep", ["1"]);
  }
  throw new Error("sandbox database did not become ready");
}

function startLogged(name, command, commandArgs, options) {
  const out = createWriteStream(path.join(logDir, `${name}.log`), { flags: "a" });
  const child = spawn(command, commandArgs, { ...options, detached: true, stdio: ["ignore", "pipe", "pipe"] });
  child.stdout.pipe(out);
  child.stderr.pipe(out);
  child.on("exit", (code) => { if (!shuttingDown && code !== null) log(`${name} exited with code ${code} (see ${name}.log)`); });
  return child;
}

async function startApi(apiEnv) {
  children.api = startLogged("api", "dotnet", ["run", "--project", apiProject, "--no-launch-profile", "--no-build"], {
    env: { ...apiEnv, BackgroundJobs__ServerEnabled: String(jobsEnabled) },
  });
  await waitFor(`${API_URL}/openapi/v1.json`, "API");
}

async function restartApi(apiEnv, enableJobs) {
  jobsEnabled = enableJobs;
  log(`restarting the API with background jobs ${jobsEnabled ? "ON" : "OFF"}`);
  await stopChild(children.api);
  await startApi(apiEnv);
  log(`API back · jobs ${jobsEnabled ? "on" : "off"}`);
}

function startControl(proxy, apiEnv) {
  const server = http.createServer(async (req, res) => {
    const reply = (status, body) => res.writeHead(status, { "content-type": "application/json" }).end(JSON.stringify(body));
    try {
      if (req.method === "GET" && req.url === "/status") return reply(200, { jobs: jobsEnabled, duplicateArmed: proxy.isDuplicateArmed() });
      if (req.method === "POST" && req.url === "/duplicate") { proxy.armDuplicate(); return reply(200, { duplicateArmed: true }); }
      if (req.method === "POST" && req.url === "/jobs/off") { await restartApi(apiEnv, false); return reply(200, { jobs: false }); }
      if (req.method === "POST" && req.url === "/jobs/on") { await restartApi(apiEnv, true); return reply(200, { jobs: true }); }
      return reply(404, { error: "unknown command" });
    } catch (error) {
      return reply(500, { error: error instanceof Error ? error.message : String(error) });
    }
  });
  server.listen(CONTROL_PORT, "127.0.0.1");
}

async function startTunnel() {
  log(`starting cloudflared quick tunnel → proxy :${PROXY_PORT}`);
  const out = createWriteStream(path.join(logDir, "tunnel.log"), { flags: "a" });
  const child = spawn("cloudflared", ["tunnel", "--no-autoupdate", "--url", `http://localhost:${PROXY_PORT}`], { detached: true, stdio: ["ignore", "pipe", "pipe"] });
  children.tunnel = child;
  await new Promise((resolve) => {
    const onData = (chunk) => {
      out.write(chunk);
      const match = String(chunk).match(/https:\/\/[a-z0-9-]+\.trycloudflare\.com/);
      if (match) {
        log(`tunnel: ${match[0]}  →  Meta callback URL: ${match[0]}/v1/webhooks/instagram`);
        resolve();
      }
    };
    child.stdout.on("data", onData);
    child.stderr.on("data", onData);
    setTimeout(resolve, 30_000);
  });
}

function listenForKeys(proxy, apiEnv) {
  log("keys: d = duplicate next delivery · j = toggle background jobs · s = status · q = quit");
  process.stdin.setRawMode(true);
  process.stdin.resume();
  process.stdin.setEncoding("utf8");
  process.stdin.on("data", async (key) => {
    if (key === "d") proxy.armDuplicate();
    if (key === "j") await restartApi(apiEnv, !jobsEnabled);
    if (key === "s") log(`status · jobs ${jobsEnabled ? "on" : "off"} · duplicate ${proxy.isDuplicateArmed() ? "armed" : "off"}`);
    if (key === "q" || key === "\u0003") await shutdown(0);
  });
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

function stopChild(child) {
  if (!child || child.exitCode !== null) return Promise.resolve();
  return new Promise((resolve) => {
    child.once("exit", () => resolve());
    try { process.kill(-child.pid, "SIGTERM"); } catch { resolve(); }
    setTimeout(() => { try { process.kill(-child.pid, "SIGKILL"); } catch { /* gone */ } resolve(); }, 10_000);
  });
}

async function shutdown(code) {
  if (shuttingDown) return;
  shuttingDown = true;
  log("stopping tunnel, web and API; stopping (not removing) the sandbox database");
  await Promise.all([stopChild(children.tunnel), stopChild(children.web), stopChild(children.api)]);
  spawnSync("docker", ["stop", SANDBOX_CONTAINER], { stdio: "ignore" });
  process.exit(code);
}

async function destroy() {
  const confirmed = args.has("--yes") || (await prompt(`Type "destroy" to remove ${SANDBOX_CONTAINER} and volume ${VOLUME}: `)) === "destroy";
  if (!confirmed) {
    log("not removed");
    return;
  }
  spawnSync("docker", ["rm", "-f", SANDBOX_CONTAINER], { stdio: "ignore" });
  spawnSync("docker", ["volume", "rm", VOLUME], { stdio: "ignore" });
  log(`removed ${SANDBOX_CONTAINER} and ${VOLUME}`);
}
