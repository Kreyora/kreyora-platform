// M08-S07 sandbox: the ONLY thing the public tunnel points at. It forwards the Instagram webhook route to the
// local API byte for byte (signature headers included) and answers 404 for everything else, so sign-in, the
// Hangfire dashboard, diagnostics and the Simulator webhook are never reachable from the internet.
// Logs carry method, path, status, size and timing only: never bodies or query strings (the verification
// handshake's query contains the verify token).
import http from "node:http";

export const ALLOWED_PATH = "/v1/webhooks/instagram";
export const MAX_BODY_BYTES = 1024 * 1024;

const HOP_BY_HOP = new Set(["connection", "keep-alive", "proxy-connection", "transfer-encoding", "upgrade", "te", "trailer", "host", "content-length"]);

/**
 * @param {{ upstream: string, log?: (line: string) => void }} options
 * @returns {{ server: http.Server, armDuplicate: () => void, isDuplicateArmed: () => boolean }}
 */
export function createWebhookProxy({ upstream, log = (line) => console.log(line) }) {
  const upstreamUrl = new URL(upstream);
  let duplicateArmed = false;

  const server = http.createServer((req, res) => {
    const started = Date.now();
    const [pathname, query = ""] = (req.url ?? "/").split("?", 2);
    const allowed = pathname === ALLOWED_PATH && (req.method === "GET" || req.method === "POST");
    if (!allowed) {
      req.resume();
      res.writeHead(404, { "content-type": "text/plain" }).end("Not found");
      log(`[proxy] ${req.method} ${sanitizePath(pathname)} 404 blocked`);
      return;
    }

    const chunks = [];
    let size = 0;
    let tooLarge = false;
    req.on("data", (chunk) => {
      size += chunk.length;
      if (size > MAX_BODY_BYTES) {
        tooLarge = true;
        return;
      }
      chunks.push(chunk);
    });
    req.on("end", async () => {
      if (tooLarge) {
        res.writeHead(413, { "content-type": "text/plain" }).end("Payload too large");
        log(`[proxy] ${req.method} ${ALLOWED_PATH} 413 ${size}B`);
        return;
      }
      const body = Buffer.concat(chunks);
      const headers = forwardHeaders(req.headers);
      const target = new URL(`${ALLOWED_PATH}${query ? `?${query}` : ""}`, upstreamUrl);
      const duplicate = req.method === "POST" && duplicateArmed;
      if (duplicate) duplicateArmed = false;

      try {
        const first = await send(target, req.method, headers, body);
        let note = "";
        if (duplicate) {
          const second = await send(target, req.method, headers, body);
          note = ` (duplicate forward: ${second.status})`;
        }
        res.writeHead(first.status, first.headers).end(first.body);
        log(`[proxy] ${req.method} ${ALLOWED_PATH} ${first.status}${note} ${body.length}B ${Date.now() - started}ms`);
      } catch {
        res.writeHead(502, { "content-type": "text/plain" }).end("Upstream unavailable");
        log(`[proxy] ${req.method} ${ALLOWED_PATH} 502 upstream unavailable ${Date.now() - started}ms`);
      }
    });
  });

  return {
    server,
    armDuplicate: () => {
      duplicateArmed = true;
      log("[proxy] duplicate armed: the next POST is forwarded twice");
    },
    isDuplicateArmed: () => duplicateArmed,
  };
}

function forwardHeaders(incoming) {
  const headers = {};
  for (const [name, value] of Object.entries(incoming)) {
    if (value === undefined || HOP_BY_HOP.has(name.toLowerCase())) continue;
    headers[name] = Array.isArray(value) ? value.join(", ") : value;
  }
  return headers;
}

async function send(target, method, headers, body) {
  const response = await fetch(target, { method, headers, body: method === "POST" ? body : undefined, redirect: "manual" });
  const responseHeaders = {};
  const contentType = response.headers.get("content-type");
  if (contentType) responseHeaders["content-type"] = contentType;
  return { status: response.status, headers: responseHeaders, body: Buffer.from(await response.arrayBuffer()) };
}

/** Blocked paths are logged without query strings and truncated, so probes can't fill the log. */
function sanitizePath(pathname) {
  return pathname.length > 80 ? `${pathname.slice(0, 80)}…` : pathname;
}
