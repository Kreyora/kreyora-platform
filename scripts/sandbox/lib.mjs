// Shared helpers for the M08-S07 sandbox tools. No dependencies.
import readline from "node:readline";

export const API_URL = process.env.SANDBOX_API_URL ?? "http://localhost:5030";

/** Visible prompt for non-secret input. */
export function prompt(question) {
  const rl = readline.createInterface({ input: process.stdin, output: process.stdout });
  return new Promise((resolve) => rl.question(question, (answer) => { rl.close(); resolve(answer.trim()); }));
}

/** Hidden prompt: nothing is echoed, and the value is never logged or written anywhere. */
export function promptHidden(question) {
  return new Promise((resolve, reject) => {
    const { stdin, stdout } = process;
    if (!stdin.isTTY) {
      reject(new Error("Hidden input needs an interactive terminal. Run this script yourself in a terminal."));
      return;
    }
    stdout.write(question);
    let value = "";
    stdin.setRawMode(true);
    stdin.resume();
    stdin.setEncoding("utf8");
    const onData = (chunk) => {
      for (const char of chunk) {
        if (char === "\r" || char === "\n") {
          finish();
          resolve(value);
          return;
        }
        if (char === "\u0003") {
          finish();
          reject(new Error("Cancelled"));
          return;
        }
        if (char === "\u007f" || char === "\b") {
          value = value.slice(0, -1);
        } else {
          value += char;
        }
      }
    };
    const finish = () => {
      stdin.off("data", onData);
      stdin.setRawMode(false);
      stdin.pause();
      stdout.write("\n");
    };
    stdin.on("data", onData);
  });
}

/** Minimal session client: cookie jar, CSRF for writes, workspace header. Prints nothing itself. */
export function createApiClient(baseUrl = API_URL) {
  const cookies = new Map();
  let csrfToken = null;
  let tenantId = null;

  async function request(method, path, body) {
    const headers = { Accept: "application/json" };
    if (method !== "GET") {
      // Fetch the token first: it also sets the antiforgery cookie that must accompany this request.
      if (!csrfToken) csrfToken = (await request("GET", "/v1/auth/csrf")).body.token;
      headers["X-CSRF-Token"] = csrfToken;
      headers["Content-Type"] = "application/json";
    }
    if (cookies.size > 0) headers.Cookie = [...cookies].map(([k, v]) => `${k}=${v}`).join("; ");
    if (tenantId) headers["X-Kreyora-Tenant-Id"] = tenantId;
    const response = await fetch(`${baseUrl}${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    for (const cookie of response.headers.getSetCookie()) {
      const [pair] = cookie.split(";");
      const index = pair.indexOf("=");
      cookies.set(pair.slice(0, index), pair.slice(index + 1));
    }
    const text = await response.text();
    let parsed = null;
    try { parsed = text ? JSON.parse(text) : null; } catch { parsed = null; }
    return { status: response.status, body: parsed };
  }

  return {
    request,
    async signIn(email, password) {
      const result = await request("POST", "/v1/auth/sign-in", { email, password });
      csrfToken = null; // antiforgery tokens are bound to the signed-in identity
      return result;
    },
    selectWorkspace(id) { tenantId = id; },
  };
}

/** Problem responses reduced to safe fields (type, title, status); never echoes request data. */
export function describeProblem(result) {
  const body = result.body ?? {};
  const code = typeof body.type === "string" && body.type.startsWith("urn:kreyora:problem:") ? body.type.slice("urn:kreyora:problem:".length) : undefined;
  // `detail` is the API's own fixed sentence (e.g. "Instagram page-link validation failed: …"); it never echoes
  // request values such as tokens or IDs.
  return [`HTTP ${result.status}`, code, body.title, typeof body.detail === "string" ? body.detail : undefined].filter(Boolean).join(" · ");
}
