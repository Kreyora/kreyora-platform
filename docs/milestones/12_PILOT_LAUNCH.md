# Milestone 12 — Controlled Pilot Launch and Learning Loop

## Objective

Launch Kreyora with 3–10 selected sellers under explicit support, safety, and measurement controls. Validate the complete social-to-order workflow in real use, learn where the product fails, and use evidence—not assumptions—to approve or reject Phase 2 investments.

## Dependencies and hard gates

- Milestone 11 exit gate approved.
- Production provider status is `READY` or pilot limitations are explicitly accepted.
- AI activation separately meets the Milestone 09 threshold; otherwise pilot with human-only inbox automation disabled.
- Seller agreement, privacy/terms, returns/payment disclosures, support process, incident owner, data-retention policy, and local legal/accounting review are complete.
- No live eSewa/Khalti or automated subscription collection is introduced here.

## Implementation design

Pilot features are controlled by tenant-scoped flags/entitlements. Cohort onboarding is staged so defects do not affect all sellers at once. Operational and product metrics are reviewed with qualitative feedback. The team must be able to disable AI/provider outbound behavior without hiding orders, inventory, messages, or audit truth.

## Step status

| Step | Description | Status |
|---:|---|---|
| 01 | Pilot charter, cohort, metrics, and stop conditions | `NOT STARTED` |
| 02 | Production readiness review and release candidate | `NOT STARTED` |
| 03 | Wave-one seller onboarding | `NOT STARTED` |
| 04 | Supported operations and incident cadence | `NOT STARTED` |
| 05 | Cohort expansion and workflow validation | `NOT STARTED` |
| 06 | Pilot retrospective and Phase 2 decision | `NOT STARTED` |

## Prompt 01 — Pilot charter, cohort, metrics, and stop conditions

> Create the pilot charter with target seller profile, 3–10 candidate cohort, product limitations, support hours/channels, owner matrix, onboarding prerequisites, data/payment responsibilities, feature flags, feedback cadence, and explicit stop/rollback conditions. Define measurable outcomes for activation, time to publish, catalog completion, enquiries, response/handoff, checkout/order conversion, order accuracy, oversell count, payment verification, fulfilment, provider failures, AI groundedness/escalation, support burden, and seller satisfaction. Define how each metric is calculated and its source. Do not invent target values; obtain approval.

**Review checkpoint:** approve cohort, responsibilities, metrics, thresholds, and stop conditions.

## Prompt 02 — Production readiness review and release candidate

> Audit all milestone exit gates and external launch gates against evidence. Produce a go/no-go matrix covering provider approval, AI approval/disable state, legal/policy content, domain/TLS, backups/restore, alerts/on-call, runbooks, secret rotation, migration, capacity, support, COD/QR procedures, tenant flags, and known defects. Build and deploy the exact release-candidate digest to staging, repeat the complete smoke path, and freeze only necessary release changes. Do not proceed to production with an unresolved no-go item.

**Review checkpoint:** obtain explicit go/no-go approval for the release candidate.

## Prompt 03 — Wave-one seller onboarding

> Onboard the smallest approved wave, preferably one or two sellers, using the real readiness path. Configure identity/team, store, catalog, inventory, delivery, COD/QR, provider connection, assistant policy/knowledge only if approved, and support contacts. Validate seller understanding of manual payment and fulfilment responsibility. Run test enquiries and test orders before enabling customer traffic. Record configuration evidence without copying secrets or unnecessary customer PII. Keep remaining cohort disabled.

**Review checkpoint:** approve each wave-one tenant independently before customer use.

## Prompt 04 — Supported operations and incident cadence

> Operate the approved wave for the charter period. Review alerts, webhook/job/DLQ state, provider health, AI tool traces/escalations, inventory reconciliation, checkout failures, manual payment-verification lag, notifications, support requests, and backups on the agreed cadence. Use runbooks and record incidents with impact, tenant scope, timeline, root cause, recovery, and follow-up. Apply kill switches when stop conditions are met. Make only approved fixes through normal CI/release controls.

**Review checkpoint:** accept operational evidence and decide whether the wave remains active, pauses, or rolls back.

## Prompt 05 — Cohort expansion and workflow validation

> If wave one meets expansion criteria, onboard the next approved seller wave. Repeat tenant readiness and test orders rather than cloning configuration blindly. Compare different catalog sizes, delivery rules, languages, enquiry patterns, and operator roles. Verify that onboarding, support load, performance, tenant isolation, inventory, payment, provider, AI/handoff, and dashboard behavior remain within accepted limits. Stop expansion when any charter condition is met.

**Review checkpoint:** approve final pilot cohort and document any feature kept disabled.

## Prompt 06 — Pilot retrospective and Phase 2 decision

> Produce a pilot retrospective using defined metrics, incident data, support themes, seller/customer feedback, technical capacity, cost/usage, and unmet external gates. Separate defects, usability problems, operational problems, missing capabilities, and speculative requests. Recommend `continue MVP`, `stabilize before growth`, `stop/pivot`, or a prioritized set of independent Phase 2 tracks. Assign each recommendation evidence, expected outcome, effort/risk range, owner, and decision. Do not automatically implement Phase 2.

**Review checkpoint:** approve the pilot outcome and explicitly authorize selected Phase 2 track(s), if any.

## Milestone exit gate

- Pilot scope, responsibilities, metrics, and stop conditions were approved before launch.
- Every enabled seller passed readiness and test-order checks.
- Production operations used controlled releases, monitoring, runbooks, backups, and incident records.
- Tenant isolation, inventory correctness, manual payment integrity, and takeover safety held in real usage.
- Pilot retrospective is evidence-based.
- Phase 2 work begins only through explicit track approval.

