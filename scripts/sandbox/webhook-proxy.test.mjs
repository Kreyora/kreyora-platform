import assert from "node:assert/strict";
import http from "node:http";
import { after, before, beforeEach, test } from "node:test";
import { ALLOWED_PATH, MAX_BODY_BYTES, createWebhookProxy } from "./webhook-proxy.mjs";

// A stub upstream stands in for the API; nothing leaves the machine.
let upstream;
let upstreamHits = [];
let proxy;
let proxyUrl;
let logs = [];

before(async () => {
  upstream = http.createServer((req, res) => {
    const chunks = [];
    req.on("data", (c) => chunks.push(c));
    req.on("end", () => {
      upstreamHits.push({ method: req.method, url: req.url, headers: req.headers, body: Buffer.concat(chunks) });
      if (req.method === "GET") {
        res.writeHead(200, { "content-type": "text/plain" }).end("challenge-echo");
      } else {
        res.writeHead(200, { "content-type": "application/json" }).end('{"ok":true}');
      }
    });
  });
  await listen(upstream);
  proxy = createWebhookProxy({ upstream: `http://127.0.0.1:${upstream.address().port}`, log: (line) => logs.push(line) });
  await listen(proxy.server);
  proxyUrl = `http://127.0.0.1:${proxy.server.address().port}`;
});

after(async () => {
  await close(proxy.server);
  await close(upstream);
});

beforeEach(() => {
  upstreamHits = [];
  logs = [];
});

test("forwards the verification handshake with its query string", async () => {
  const response = await fetch(`${proxyUrl}${ALLOWED_PATH}?hub.mode=subscribe&hub.verify_token=secret-token&hub.challenge=42`);

  assert.equal(response.status, 200);
  assert.equal(await response.text(), "challenge-echo");
  assert.equal(upstreamHits.length, 1);
  assert.equal(upstreamHits[0].url, `${ALLOWED_PATH}?hub.mode=subscribe&hub.verify_token=secret-token&hub.challenge=42`);
});

test("forwards a delivery byte for byte, including the signature header", async () => {
  const body = Buffer.from('{"object":"instagram","entry":[{"id":"1","time":1}]}  ');

  const response = await fetch(`${proxyUrl}${ALLOWED_PATH}`, {
    method: "POST",
    headers: { "content-type": "application/json", "x-hub-signature-256": "sha256=abc123" },
    body,
  });

  assert.equal(response.status, 200);
  assert.equal(upstreamHits.length, 1);
  assert.deepEqual(upstreamHits[0].body, body);
  assert.equal(upstreamHits[0].headers["x-hub-signature-256"], "sha256=abc123");
});

for (const [method, path] of [
  ["POST", "/v1/auth/sign-in"],
  ["GET", "/hangfire"],
  ["POST", "/v1/webhooks/simulator"],
  ["GET", "/v1/integrations/diagnostics/overview"],
  ["POST", `${ALLOWED_PATH}/extra`],
  ["PUT", ALLOWED_PATH],
  ["DELETE", ALLOWED_PATH],
]) {
  test(`blocks ${method} ${path} without touching the API`, async () => {
    const response = await fetch(`${proxyUrl}${path}`, { method, body: method === "GET" ? undefined : "{}" });

    assert.equal(response.status, 404);
    assert.equal(upstreamHits.length, 0);
  });
}

test("duplicate mode forwards exactly the next delivery twice, then turns itself off", async () => {
  proxy.armDuplicate();
  const body = '{"object":"instagram"}';
  const send = () => fetch(`${proxyUrl}${ALLOWED_PATH}`, { method: "POST", headers: { "x-hub-signature-256": "sha256=sig" }, body });

  assert.equal((await send()).status, 200);
  assert.equal(upstreamHits.length, 2);
  assert.deepEqual(upstreamHits[0].body, upstreamHits[1].body);
  assert.equal(upstreamHits[1].headers["x-hub-signature-256"], "sha256=sig");
  assert.equal(proxy.isDuplicateArmed(), false);

  await send();
  assert.equal(upstreamHits.length, 3);
});

test("rejects oversized bodies without forwarding", async () => {
  const response = await fetch(`${proxyUrl}${ALLOWED_PATH}`, { method: "POST", body: Buffer.alloc(MAX_BODY_BYTES + 1, 97) });

  assert.equal(response.status, 413);
  assert.equal(upstreamHits.length, 0);
});

test("logs never contain bodies, query strings or tokens", async () => {
  await fetch(`${proxyUrl}${ALLOWED_PATH}?hub.verify_token=secret-token&hub.challenge=42`);
  await fetch(`${proxyUrl}${ALLOWED_PATH}`, { method: "POST", body: '{"text":"private customer message"}' });
  await fetch(`${proxyUrl}/v1/auth/sign-in?password=hunter2`, { method: "POST", body: '{"password":"hunter2"}' });

  const joined = logs.join("\n");
  assert.equal(logs.length, 3);
  assert.doesNotMatch(joined, /secret-token|challenge=|private customer message|hunter2/);
});

test("answers 502 when the API is down", async () => {
  const down = createWebhookProxy({ upstream: "http://127.0.0.1:9", log: () => {} });
  await listen(down.server);
  try {
    const response = await fetch(`http://127.0.0.1:${down.server.address().port}${ALLOWED_PATH}`, { method: "POST", body: "{}" });
    assert.equal(response.status, 502);
  } finally {
    await close(down.server);
  }
});

function listen(server) {
  return new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
}

function close(server) {
  return new Promise((resolve) => server.close(() => resolve()));
}
