#!/usr/bin/env node
// M08-S07 sandbox evidence: read-only aggregate queries against the sandbox database. Prints counts, statuses,
// event types, delivery states and the longest observed Instagram message ID length. Never prints identifiers,
// names, tokens or message content, so the output can go straight into the checkpoint.
import { spawnSync } from "node:child_process";

export const SANDBOX_CONTAINER = "kreyora-sandbox-pg";
export const SANDBOX_DATABASE = "kreyora_sandbox";

export const QUERIES = [
  ["Connections (channel · status)", "select channel, status, count(*) from channel_connections group by 1, 2 order by 1, 2"],
  ["Webhook events (channel · processing status)", "select channel, processing_status, count(*) from webhook_events group by 1, 2 order by 1, 2"],
  ["Webhook events (event type)", "select coalesce(event_type, '(none)'), count(*) from webhook_events group by 1 order by 1"],
  ["Dead letters (failure classification)", "select coalesce(failure_classification, '(none)'), count(*) from webhook_events where processing_status = 'DeadLetter' group by 1 order by 1"],
  ["Inbound events (event type)", "select event_type, count(*) from inbound_events group by 1 order by 1"],
  ["Inbound events with duplicate dedup keys (must be 0)", "select count(*) from (select deduplication_key from inbound_events group by connection_id, deduplication_key having count(*) > 1) d"],
  ["Conversations (channel · status · automation)", "select channel, status, automation_mode, count(*) from conversations group by 1, 2, 3 order by 1, 2, 3"],
  ["Messages (direction · origin · delivery status)", "select direction, coalesce(origin, '-'), coalesce(delivery_status, '-'), count(*) from messages group by 1, 2, 3 order by 1, 2, 3"],
  ["Messages with duplicate provider IDs per connection (must be 0)", "select count(*) from (select provider_message_id from messages where provider_message_id is not null group by connection_id, provider_message_id having count(*) > 1) d"],
  ["Reactions (active · removed)", "select case when is_removed then 'removed' else 'active' end, count(*) from message_reactions group by 1 order by 1"],
  ["Longest Instagram message ID (characters)", "select coalesce(max(length(m.provider_message_id)), 0) from messages m join channel_connections c on c.id = m.connection_id where c.channel = 'Instagram'"],
  ["Outbound messages (origin · status)", "select origin, status, count(*) from outbound_messages group by 1, 2 order by 1, 2"],
  ["Delivery attempts (succeeded · provider error code)", "select succeeded, coalesce(provider_error_code, '-'), count(*) from outbound_delivery_attempts group by 1, 2 order by 1, 2"],
];

export function runQuery(sql) {
  const result = spawnSync("docker", ["exec", SANDBOX_CONTAINER, "psql", "-U", "postgres", "-d", SANDBOX_DATABASE, "-At", "-F", " | ", "-c", sql], { encoding: "utf8" });
  if (result.status !== 0) throw new Error((result.stderr || "psql failed").trim().split("\n")[0]);
  return result.stdout.trim();
}

if (import.meta.url === `file://${process.argv[1]}`) {
  console.log(`## Sandbox evidence (${new Date().toISOString()})\n`);
  for (const [title, sql] of QUERIES) {
    let output;
    try {
      output = runQuery(sql) || "(none)";
    } catch (error) {
      output = `query failed: ${error.message}`;
    }
    console.log(`**${title}**\n\n\`\`\`\n${output}\n\`\`\`\n`);
  }
}
