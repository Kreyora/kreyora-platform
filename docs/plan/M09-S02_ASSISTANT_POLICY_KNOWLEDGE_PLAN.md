# M09-S02 — Assistant Policy and Approved Knowledge Lifecycle — Scoped Plan

## Identity

- **Milestone:** 09 — Constrained AI Assistant, RAG, and Commerce Tools
- **Step:** 02 — Assistant policy and approved knowledge lifecycle
- **Author:** Claude (planning)
- **Date:** 2026-10-06
- **Status:** `APPROVED` 2026-10-06; was `REVIEW` (2026-10-06, checkpoint `artifacts/checkpoints/M09-S02.md`); was `IN PROGRESS` — approved by the owner on 2026-10-06 ("ok implement"): Q1–Q8 as recommended, plus media decisions M1–M3
- **Prerequisites:** M09-S01 `APPROVED` (2026-10-06); ADR-018 `Accepted`.
- **Manual work for the owner in this step:**
  - answer the decisions below;
  - optional: send sample FAQ / delivery / returns wording (yours or made-up) so the defaults and tests read like a real Nepali shop;
  - no keys, no Meta, no money.

## Milestone prompt (verbatim)

> Implement tenant AssistantPolicy with activation state, supported languages, tone/brand constraints, business hours, escalation rules, allowed tools, maximum budgets, and safe defaults. Implement KnowledgeDocument lifecycle from upload/reference through validation, extraction, review, approval, active version, supersession, rejection, and deletion. Only approved active content may be retrieved. Reuse secure media/storage paths, enforce type/size limits, and avoid sending raw unsupported documents to the model. Add authorization, audit, migrations, APIs, and lifecycle tests.

**Review checkpoint:** approve safe defaults, activation/readiness checks, document approval workflow, and data handling.

## What exists today (verified 2026-10-06)

- **Permissions:** `ai.configuration.read` (Owner, Admin, Operator, Viewer) and `ai.configuration.write` (Owner, Admin) already exist in `TenantPermissions`.
- **Store data the assistant can use:**
  - `Store` holds `ReturnsPolicy`, `PaymentPolicy`, `TermsPolicy`, `PrivacyPolicy` and contact details;
  - `DeliveryRule` / `DeliveryRuleZone`;
  - `StorePaymentConfiguration` (COD / merchant QR);
  - `StoreProductPublication`.
- **Readiness:** `IStorefrontAdministrationService.GetReadinessAsync()` returns `StoreReadiness` (`CanActivate`, `CanAcceptOrders`, sections, blockers). The assistant reuses it rather than defining "setup complete" twice.
- **Storage:** `IPrivateObjectStorage` (put / metadata / read / delete) with tenant-scoped object keys (M04 media path). `IAuditEventService` for audit.
- **AI boundary (S01):**
  - `IAiChatClient`, kill switch `Ai:Enabled` (operator), data policy;
  - per-conversation automation and takeover from M08 (ADR-017).
- **Frontend:** demo-only `AssistantConfig` / `KnowledgeDocument` types and screens. Real clients come in S08 (milestone sequencing); **S02 is backend + API only.**
- **Owner preferences already recorded:**
  - AI on by default per chat;
  - per shop **on automatically once setup is complete**;
  - reply style option because of Romanized/Devanagari slips (ADR-018 §4, §7).

## Objective

Give every shop a **safe, editable assistant policy** and a **reviewed knowledge library**. Only content an owner/admin has approved can ever reach the AI, and the policy decides when the assistant may answer at all.

## Design

### A. AssistantPolicy (one per workspace)

Created lazily with **safe defaults** on first read.

| Setting | Default | Notes |
|---|---|---|
| `Enabled` (seller toggle) | **true** (Q1) | Effective activation also needs readiness and the platform kill switch (C) |
| `ReplyStyle` | **`MatchCustomer`** (Q2) | `MatchCustomer` / `AlwaysRomanized` / `AlwaysDevanagari` / `AlwaysEnglish` (ADR-018 §7) |
| `SupportedLanguages` | Nepali, Romanized Nepali, English | Messages in other languages are escalated |
| `Tone` | `Friendly` | `Friendly` / `Formal` |
| `BrandNote` | empty | Optional, ≤ 300 characters, plain text (e.g. "We call customers 'hajur'"). Stored as data and passed to the model inside clear delimiters, never as instructions that override the rules |
| `BusinessHours` | Every day 09:00–19:00, Asia/Kathmandu | Per weekday open/close or closed |
| `OutsideHoursBehavior` | **`AnswerAndPromiseFollowUp`** (Q6) | `AnswerNormally` / `AnswerAndPromiseFollowUp` / `DoNotAnswer` |
| `EscalationRules` | Fixed safe set **always on**: customer asks for a person, complaint, refund/exchange, custom or wholesale order, health/safety, legal, payment dispute, abusive message | Plus up to 20 seller keywords ("escalate if the message mentions …"). The fixed set cannot be switched off |
| `UnrecognizedMediaBehavior` | **`AskForDetails`** (M3) | When a customer sends a photo, screenshot or link the assistant cannot identify: `AskForDetails` (ask for the product name or to share the shop's post) or `HandToPerson` (escalate). The assistant never guesses |
| `AllowedTools` | Read tools on (`SearchProducts`, `CheckInventory`, `GetPrice`, `GetShippingInfo`, `GetOrderStatus`) plus `EscalateToHuman` (always); write tools **off** | Validated against a known tool catalog. Write tools become selectable in S05 |
| `Budgets` | Max tool steps per reply 4; max replies per conversation per hour 20; max output tokens 600 | **Platform caps clamp** tenant values (e.g. tool steps ≤ 6, output ≤ `Ai:Limits:MaxOutputTokens`). Plan entitlements are M10 |

- Optimistic concurrency (`xmin`).
- Every change is **audited** (`assistant.policy.updated`, changed field names only).

### B. Knowledge library: documents with versions

**KnowledgeDocument:** tenant, title, `Category` (`Faq` / `Delivery` / `Returns` / `Payment` / `Brand` / `Other`), `Source` (`Text` / `Upload` / `StorePolicy`), current active version, soft-delete.

**KnowledgeDocumentVersion:**
- version number, state, content hash, character count;
- **extracted plain text** (stored in the database, ≤ 30,000 characters);
- the original upload in **private object storage** (`tenants/{tenantId}/assistant-knowledge/{documentId}/{versionId}`);
- submitted by and at; reviewed by and at; review note.

**Lifecycle** (milestone wording mapped to states):

```
submit (text / upload / store policy)
  → validation + extraction   (synchronous for supported types; failures are rejected immediately with a reason)
  → PendingReview
  → Approved  ⇒ becomes the Active version; the previous Active becomes Superseded
  → or Rejected (with note)
Document delete ⇒ all versions Deleted; uploaded originals purged from storage; extracted text cleared
```

**Rules:**
- **Only one Active version** per document (unique partial index).
- Approving a version supersedes the old one in the same transaction.
- Rejected and Superseded versions are kept for history (text kept, never retrievable). Deleted versions keep metadata only.
- Edits create a new version; the active version keeps serving until the new one is approved.

**Supported inputs (Q3):**
- pasted text;
- `.txt` and `.md` uploads (UTF-8; ≤ 256 KB file; ≤ 30,000 characters after extraction).
- **PDF and Word are rejected** in S02 with a clear message ("Paste the text instead"), so the model never receives raw unsupported documents.
- Binary content, control characters and empty results are rejected.
- **No URL fetching** (avoids server-side request forgery).

**Store policy import (Q7):** one action creates **PendingReview drafts** from the Store's Returns / Payment / Terms policy text, so the owner reviews and approves instead of retyping. Re-importing after a policy change creates a new version.

**Approval (Q4/Q5):**
- submit, approve and reject need `ai.configuration.write` (Owner/Admin);
- the same person may submit and approve (small shops), always audited;
- Operators and Viewers can read the library.

**Audit:** `assistant.knowledge.submitted`, `.approved`, `.rejected`, `.deleted`, `.imported`. Metadata only, never document text.

### C. Readiness and effective activation

`GET /v1/assistant/readiness` returns checks with codes; **effective activation = seller `Enabled` AND all required checks pass AND platform `Ai:Enabled`**.

| Check | Required? | Source |
|---|---|---|
| Store is active and can accept orders (published products, delivery rules, payment method) | Required | `StoreReadiness.CanAcceptOrders` |
| An active social channel connection exists | Required | Channel connections (M07/M08) |
| Assistant policy reviewed (owner saved it at least once) | Required (Q8) | AssistantPolicy |
| At least one approved knowledge document | **Recommended** (warning, not blocking) | Knowledge library |
| Platform AI switch | Informational | `Ai:Enabled` (operator) |

- Readiness is **computed on read**: no stored flag, so no drift.
- With the owner's preference (Q1), a shop becomes active **automatically** when the required checks pass, unless the seller turned it off.
- **Nothing in S02 sends anything to the model.** S07 uses `IAssistantActivationQuery` before invoking AI.

### D. Query contract for later steps

`IApprovedKnowledgeQuery` (Application) returns **only Active, approved, non-deleted** versions for the current tenant (text + document/version IDs + category).
- S03 (retrieval) indexes from it, and reconciles on version changes via a version stamp.
- S06 (orchestration) reads the policy through `IAssistantPolicyQuery`.
- Neither touches repositories directly.

### E. API (all tenant-scoped, RFC 7807 errors, antiforgery on writes)

| Method & route | Permission | Purpose |
|---|---|---|
| `GET /v1/assistant/policy` | read | Policy (defaults created on first read) |
| `PUT /v1/assistant/policy` | write | Update; `xmin` version → 409 on concurrent change; validation codes |
| `GET /v1/assistant/readiness` | read | Checks + effective activation |
| `GET /v1/assistant/knowledge` | read | Documents with active/pending version summaries |
| `POST /v1/assistant/knowledge` | write | Create from pasted text (→ PendingReview) |
| `POST /v1/assistant/knowledge/upload` | write | `.txt`/`.md` multipart upload (→ PendingReview or rejected with reason) |
| `POST /v1/assistant/knowledge/{id}/versions` | write | New version (text or upload) |
| `GET /v1/assistant/knowledge/{id}/versions/{versionId}` | read | Version detail incl. text, for review |
| `POST /v1/assistant/knowledge/{id}/versions/{versionId}/approve` | write | Approve → Active (supersedes previous) |
| `POST /v1/assistant/knowledge/{id}/versions/{versionId}/reject` | write | Reject with note |
| `DELETE /v1/assistant/knowledge/{id}` | write | Delete document (purge originals) |
| `POST /v1/assistant/knowledge/import-store-policies` | write | Drafts from Store policies |

Writes that create content accept an `Idempotency-Key` so a retried upload doesn't create duplicate versions.

## Tests

**Integration (real PostgreSQL / Testcontainers) and unit:**
- **Lifecycle:**
  - submit → approve → active;
  - a new version approved supersedes the old;
  - reject keeps the active version;
  - invalid transitions refused (approve a rejected/superseded/deleted version; approve twice);
  - only one Active per document under **concurrent approvals** (unique index; one wins).
- **Retrieval guard:** `IApprovedKnowledgeQuery` never returns PendingReview, Rejected, Superseded or Deleted content, or another tenant's.
- **Validation:** PDF/DOCX/binary rejected with reasons; oversize file and over-limit characters; empty; invalid UTF-8; control characters stripped; idempotent upload retry.
- **Deletion:** original removed from storage (fake storage asserts), text cleared, document hidden, audit written.
- **Authorization:** Viewer/Operator cannot write or approve; unauthenticated 401; cross-tenant IDs → 404.
- **Policy:**
  - defaults are safe (write tools off, fixed escalation set present);
  - platform caps clamp budgets;
  - unknown tools / languages / reply styles rejected;
  - brand note limits;
  - concurrency 409;
  - audit records field names only.
- **Readiness:** each check on/off; effective activation truth table (seller toggle × required checks × platform switch).
- **Store policy import:** creates drafts, never auto-approves; re-import makes a new version.
- **Migration:** additive (three new tables), applied by Testcontainers.

## Contracts and migrations

- New endpoints (above).
- New tables: `assistant_policies`, `knowledge_documents`, `knowledge_document_versions` (additive migration).
- OpenAPI/TS regenerated (additive).
- No change to existing contracts.

## Security and data handling

- Tenant isolation on every query: query filters + explicit ownership checks; object keys include the tenant.
- Knowledge text is seller-provided business content. It is **never logged or audited**; audit stores metadata only.
- Free-tier rule (ADR-018): real shop content is only sent to a model at activation, on a paid, approved endpoint (S08). In development, knowledge used with the free tier is synthetic.
- The brand note and knowledge are treated as **untrusted data** in later prompts (S03/S06 test malicious document instructions); S02 bounds their size.
- Only supported text types are stored; nothing else ever reaches extraction or the model.

## Acceptance criteria

1. The policy API with safe defaults, validation, platform caps, concurrency and audit.
2. The knowledge lifecycle (submit/upload/import → validation/extraction → review → approve/reject → active/superseded → delete), with storage purge and audit.
3. The approved-only query contract for S03/S06, proven by tests (states + tenants).
4. Readiness and effective activation per the truth table; reuses `StoreReadiness`.
5. Additive migration; OpenAPI regenerated; full gates green (backend, EF, `ci:frontend`, `git diff --check`); Docker at baseline.
6. Checkpoint `M09-S02.md` (`REVIEW`).

## Decisions for the owner

| # | Decision | Recommendation |
|---|---|---|
| Q1 | Seller toggle default | **On**: the assistant activates automatically once required checks pass; the seller can turn it off (your stated preference) |
| Q2 | Default reply style | **Match the customer** (Romanized ↔ Romanized, Devanagari ↔ Devanagari). Sellers can switch to "always Romanized" |
| Q3 | Knowledge input types in S02 | **Pasted text + `.txt`/`.md` uploads**; PDF/Word rejected with "paste the text" (PDF extraction needs a library and has its own risks, so later if needed) |
| Q4 | Who submits and approves knowledge | **Owner and Admin** (`ai.configuration.write`); Operators and Viewers read only |
| Q5 | May the same person submit and approve? | **Yes** (small shops), always audited |
| Q6 | Outside business hours | **Answer, and say a team member will follow up during opening hours** |
| Q7 | Import Store policies as drafts | **Yes**: Returns / Payment / Terms become PendingReview drafts for approval |
| Q8 | Must the owner save the assistant policy once before activation? | **Yes**: a one-click "review and save" so nobody goes live on defaults they never saw |

## Out of scope

- Embeddings and retrieval (S03); tools (S04/S05); orchestration and budgets enforcement at runtime (S06); conversation wiring (S07); frontend screens (S08).
- PDF/Word extraction; URL import; plan entitlements (M10).

## Build checklist

- [x] Task 1 — Domain: AssistantPolicy (defaults, validation, caps), KnowledgeDocument / Version (lifecycle rules)
- [x] Task 2 — EF configuration + additive migration (unique active-version index; tenant filters)
- [x] Task 3 — Services: policy (get/update/audit), knowledge (submit/upload/import/approve/reject/delete; storage; idempotency), readiness/activation, query contracts
- [x] Task 4 — Controllers + RFC 7807 codes + antiforgery; OpenAPI/TS regeneration
- [x] Task 5 — Tests (unit + Testcontainers integration, incl. concurrency and cross-tenant)
- [x] Task 6 — Full gates; Docker check; checkpoint `M09-S02.md` (`REVIEW`); status docs

### Approval (2026-10-06)

The owner approved ("ok implement"): Q1–Q8 as recommended, and the media decisions from the same discussion:
- **M1:** exact matching of **shared Instagram posts/reels and Kreyora storefront links** to products: seller links posts to products once, plus a "resolve reference" read tool. **Scheduled for M09-S04** (recorded here; built there).
- **M2:** **photo/screenshot recognition stays deferred** until after M09 (Phase 2), as `plan.md` §10.13 already says; lifting it requires an ADR. Gemini is multimodal; a later design would propose catalog matches and require customer confirmation before any price, on the paid tier only (screenshots can contain personal data).
- **M3:** new policy setting `UnrecognizedMediaBehavior` (default `AskForDetails`), added to section A.
- **To verify live:** what Instagram's shared-post webhook payload contains (permalink / post ID vs image URL only), in the M09-S07 sandbox session or earlier.
