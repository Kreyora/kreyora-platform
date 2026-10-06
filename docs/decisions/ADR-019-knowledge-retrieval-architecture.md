# ADR-019 — Knowledge retrieval architecture

- **Status:** `Proposed` (owner acceptance at the M09-S03 checkpoint)
- **Date:** 2026-10-06
- **Owner:** Platform Architect
- **Reviewers:** Project Owner
- **Affected milestones:** M09 (S03 retrieval, S06 orchestration, S08 evaluation); M11 (operations)

## Context

M09-S03 must return approved knowledge passages for a customer message, isolated per tenant and approval state, **filtering before similarity ranking** (milestone prompt). The milestone requires choosing "PostgreSQL vector support or another architecture only through an ADR with operational consequences".

**Facts:**
- Knowledge is small per shop: S02 caps each document at 30,000 characters, and shops have a handful of documents. That's tens to a few hundred chunks per tenant.
- Customers write Romanized Nepali, Devanagari and English, while FAQs are often English. Lexical search cannot bridge that.
- **Embedding probe** (2026-10-06, synthetic text, owner's Google AI Studio free key, OpenAI-compatible `/embeddings`):
  - `gemini-embedding-001` / `-2` are available; 3,072 dimensions by default; **`dimensions: 768` is honored**.
  - At 768 dimensions vectors are **not unit-length** (norm ≈ 0.59), so true cosine must be computed.
  - Romanized "Pokhara ma delivery kati din lagcha?" vs English delivery FAQ: **0.80** (vs 0.54 for the returns FAQ).
  - Devanagari returns question vs English returns FAQ: 0.67–0.69 vs 0.62–0.63 (narrow margin).
  - Unrelated question ≈ 0.49–0.54.
- **Infrastructure:** `postgres:16-alpine` everywhere (Compose, Testcontainers). pgvector is not installed.
- **ADR-018 applies:** kill switch, provider-neutral boundary, free tier synthetic only.

## Decision

1. **Storage:** chunks in table `knowledge_chunks` with version metadata and an optional `real[]` embedding (`EmbeddingModel`, `EmbeddingDimensions`, `IndexedAt`). **No database extension.**
2. **Ranking in the application, after a SQL filter.**
   - The SQL query selects only chunks whose `VersionId` equals the document's `ActiveVersionId`, with the version `Active`, the document not deleted, and the tenant equal to the trusted current tenant.
   - The application then scores **only those candidates**.
   - The filter-before-ranking guarantee is therefore structural; it doesn't depend on chunk clean-up.
3. **Score:** cosine similarity between query and chunk embeddings, plus a small lexical-overlap boost (`Ai:Retrieval:LexicalWeight`, default 0.1).
   - **Thresholds:** relevant ≥ 0.62; low confidence 0.55–0.62; otherwise no result. Tuned with live data and finalized in S08.
   - **Top 4 passages, ≤ 3,000 characters.**
4. **Lexical fallback:** when embeddings are unavailable (AI disabled, provider failure, quota, not indexed yet), retrieval ranks by lexical overlap with its own thresholds and reports `Mode = LexicalFallback`. It degrades instead of failing.
5. **Embeddings:**
   - `IAiEmbeddingClient` (provider-neutral); OpenAI-compatible client (Gemini today; others by configuration, R-PAID).
   - **Offline deterministic `FakeEmbeddingClient`** for tests and `Ai:Mode=Fake`.
   - Model `gemini-embedding-001`, 768 dimensions.
   - Embedding obeys the kill switch, so no shop content is sent while AI is disabled.
6. **Lifecycle:**
   - **Approval:** chunks are created in the approval transaction (lexical retrieval works immediately); embeddings are added asynchronously (`KnowledgeIndexingJob`, immediate enqueue + recurring sweeper).
   - **Supersede / delete:** that version's chunks are removed in the same transaction.
   - **Sweeper:** embeds missing vectors, **re-embeds on model or dimension change** (the reindex workflow), removes orphan chunks and purges orphaned original files.
7. **Migration trigger to pgvector:** move to pgvector (or another vector index) when **any tenant exceeds about 2,000 active chunks, or p95 retrieval latency exceeds 50 ms**. The retrieval interface stays the same; only the candidate-scoring implementation changes, and it must keep the tenant/approval filter **inside** the indexed query.

## Alternatives considered

| Option | Benefits | Costs/risks | Reason rejected or deferred |
|---|---|---|---|
| pgvector now | Index-backed search at scale | New image in Compose/CI/prod; managed PostgreSQL must support the extension; filtered approximate search must be designed so the tenant filter applies before ranking | Deferred until the trigger above; unnecessary at shop scale |
| Lexical only (full-text / trigram) | $0, no external calls | Cannot match Romanized/Devanagari questions to English FAQs (the common case) | Rejected as the primary method; kept as the fallback |
| Put all approved knowledge into every prompt | No index at all | Token cost grows with knowledge size; no citations or thresholds; harder to evaluate | Rejected |
| External vector database | Scales | New service, cost, data leaves PostgreSQL; tenant isolation becomes a second system | Rejected for MVP |

## Consequences

- **Product impact:** cross-language FAQ answers. When nothing is relevant, retrieval says so (so S06 escalates or answers "a team member will confirm"). Owners get a search test console.
- **Architecture impact:**
  - new `knowledge_chunks` table;
  - `IAiEmbeddingClient`, `IKnowledgeRetrievalService`, `IKnowledgeIndexingService`;
  - recurring job `knowledge-indexing`;
  - the S02 approval/delete paths maintain chunks.
- **Security/privacy impact:**
  - tenant from trusted context only; filter before ranking (tested, including a higher-similarity forbidden chunk);
  - passages are returned as untrusted data;
  - vectors are derived content and are removed with their versions;
  - no shop content goes to the provider while AI is disabled;
  - free tier synthetic only (ADR-018).
- **Cost/operations impact:**
  - storage about 3 KB per chunk (768 × 4 bytes);
  - embedding cost on paid Gemini is negligible at shop scale (indexing happens once per approved version);
  - no new infrastructure.
- **Migration or rollback impact:** additive migration. Rollback = disable AI (lexical fallback only) or drop the chunk table. The pgvector move is a future additive migration.

## Validation evidence

- Integration tests (real PostgreSQL, fake embedder):
  - cross-tenant collisions with identical text;
  - superseded / rejected / deleted / leftover chunks never returned;
  - a forbidden chunk with higher similarity still not returned;
  - empty and low-confidence;
  - malicious instructions returned only as cited untrusted data;
  - citation text equals the exact source range.
- Unit tests: chunker (determinism, overlap, Devanagari), scoring and thresholds, fake embedder, embedding client contract shapes.
- Live probe evidence above; optional live retrieval check via the evaluation harness.

## Supersession conditions

- The pgvector trigger is met.
- S08 evaluation shows retrieval quality below threshold with this design.
- Google changes or retires the embedding model.
- The owner chooses a provider without an OpenAI-compatible embeddings endpoint (that needs a new adapter, not a new ADR, unless the architecture changes).
