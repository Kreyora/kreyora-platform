import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const defaultSpecUrl = "http://127.0.0.1:5001/openapi/v1.json";
const defaultServerUrl = "http://localhost:5030/";
const specUrl = process.env.KREYORA_OPENAPI_URL ?? defaultSpecUrl;
const snapshotServerUrl = process.env.KREYORA_OPENAPI_SNAPSHOT_SERVER_URL ?? defaultServerUrl;
const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const outputPath = join(scriptDirectory, "..", "src", "lib", "api", "generated", "openapi-v1.json");

const response = await fetch(specUrl, {
  headers: { Accept: "application/json" },
});

if (!response.ok) {
  throw new Error(`OpenAPI download failed with ${response.status} ${response.statusText}: ${specUrl}`);
}

const document = await response.json();
if (Array.isArray(document.servers)) {
  document.servers = document.servers.map((server) => ({
    ...server,
    url: snapshotServerUrl,
  }));
}
await mkdir(dirname(outputPath), { recursive: true });
await writeFile(outputPath, `${JSON.stringify(document, null, 2)}\n`, "utf8");

console.log(`Wrote OpenAPI snapshot from ${specUrl}`);
