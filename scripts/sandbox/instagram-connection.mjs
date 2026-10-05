#!/usr/bin/env node
// M08-S07 sandbox: owner-run connect / reauthorize for the Instagram connection (Development tool; real
// onboarding is M10). Run it yourself in a terminal: the Page token and Meta IDs are typed into hidden prompts,
// sent only to the local API (which validates the token live against Graph and encrypts it), and never echoed,
// logged or written to disk. Output is limited to outcome codes and Kreyora connection status.
//
//   node scripts/sandbox/instagram-connection.mjs connect
//   node scripts/sandbox/instagram-connection.mjs reauthorize
//   node scripts/sandbox/instagram-connection.mjs list
import { API_URL, createApiClient, describeProblem, prompt, promptHidden } from "./lib.mjs";

const command = process.argv[2];
if (!["connect", "reauthorize", "list"].includes(command ?? "")) {
  console.log("Usage: node scripts/sandbox/instagram-connection.mjs <connect|reauthorize|list>");
  process.exit(2);
}

try {
  const api = await signInAndSelectWorkspace();
  if (command === "list") await list(api);
  if (command === "connect") await connect(api);
  if (command === "reauthorize") await reauthorize(api);
} catch (error) {
  console.error(`Stopped: ${error instanceof Error ? error.message : String(error)}`);
  process.exit(1);
}

async function signInAndSelectWorkspace() {
  console.log(`Kreyora API: ${API_URL}`);
  const api = createApiClient();
  const email = await prompt("Kreyora email: ");
  const password = await promptHidden("Kreyora password (hidden): ");
  const signIn = await api.signIn(email, password);
  if (signIn.status !== 204) throw new Error(`sign-in failed (${describeProblem(signIn)})`);

  const workspaces = await api.request("GET", "/v1/workspaces");
  const items = Array.isArray(workspaces.body) ? workspaces.body : [];
  if (items.length === 0) throw new Error("this account has no workspace");
  let chosen = items[0];
  if (items.length > 1) {
    items.forEach((w, i) => console.log(`  ${i + 1}. ${w.displayName} (${w.role})`));
    const pick = Number(await prompt("Workspace number: "));
    chosen = items[pick - 1];
    if (!chosen) throw new Error("no such workspace");
  }
  api.selectWorkspace(chosen.tenantId);
  console.log(`Workspace: ${chosen.displayName}`);
  return api;
}

async function instagramConnections(api) {
  const result = await api.request("GET", "/v1/integrations/connections");
  if (result.status !== 200) throw new Error(`could not list connections (${describeProblem(result)})`);
  const items = Array.isArray(result.body) ? result.body : result.body?.items ?? [];
  return items.filter((c) => String(c.channel).toLowerCase() === "instagram");
}

async function list(api) {
  const connections = await instagramConnections(api);
  if (connections.length === 0) console.log("No Instagram connection in this workspace.");
  connections.forEach((c) => console.log(`  ${c.displayName} · status ${c.status} · connection ${c.id}`));
}

async function readMetaInputs() {
  const pageId = await promptHidden("Facebook Page ID (hidden): ");
  const instagramAccountId = await promptHidden("Instagram account ID (hidden): ");
  const pageToken = await promptHidden("Page access token (hidden): ");
  if (!pageId || !instagramAccountId || !pageToken) throw new Error("all three values are required");
  return { pageId, instagramAccountId, pageToken };
}

async function connect(api) {
  const displayName = (await prompt("Display name [Instagram sandbox]: ")) || "Instagram sandbox";
  const { pageId, instagramAccountId, pageToken } = await readMetaInputs();
  console.log("Validating the token with Meta through the local API…");
  const result = await api.request("POST", "/v1/integrations/connections", {
    channel: "instagram",
    externalAccountId: instagramAccountId,
    displayName,
    plainTextSecret: pageToken,
    instagram: { pageId, instagramAccountId },
  });
  if (result.status !== 201 && result.status !== 200) throw new Error(`connect refused (${describeProblem(result)})`);
  console.log(`Connected · status ${result.body?.status} · connection ${result.body?.id}`);
}

async function reauthorize(api) {
  const connections = await instagramConnections(api);
  if (connections.length === 0) throw new Error("no Instagram connection to reauthorize; run connect first");
  connections.forEach((c, i) => console.log(`  ${i + 1}. ${c.displayName} · status ${c.status}`));
  const target = connections.length === 1 ? connections[0] : connections[Number(await prompt("Connection number: ")) - 1];
  if (!target) throw new Error("no such connection");

  const { pageId, instagramAccountId, pageToken } = await readMetaInputs();
  console.log("Validating the new token with Meta through the local API…");
  const result = await api.request("PUT", `/v1/integrations/connections/${target.id}`, {
    plainTextSecret: pageToken,
    instagram: { pageId, instagramAccountId },
  });
  if (result.status !== 200) throw new Error(`reauthorize refused (${describeProblem(result)})`);
  console.log(`Reauthorized · status ${result.body?.status} · connection ${result.body?.id}`);
}
