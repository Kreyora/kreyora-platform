#!/usr/bin/env node
// M08-S07 sandbox control, for a runner that is already running (local only, 127.0.0.1:5039).
//   node scripts/sandbox/ctl.mjs status | duplicate | jobs-off | jobs-on
const routes = { status: ["GET", "/status"], duplicate: ["POST", "/duplicate"], "jobs-off": ["POST", "/jobs/off"], "jobs-on": ["POST", "/jobs/on"] };
const route = routes[process.argv[2] ?? ""];
if (!route) {
  console.log(`Usage: node scripts/sandbox/ctl.mjs <${Object.keys(routes).join("|")}>`);
  process.exit(2);
}
try {
  const response = await fetch(`http://127.0.0.1:5039${route[1]}`, { method: route[0] });
  console.log(JSON.stringify(await response.json()));
  process.exit(response.ok ? 0 : 1);
} catch {
  console.error("The sandbox runner is not running (node scripts/sandbox/run.mjs).");
  process.exit(1);
}
