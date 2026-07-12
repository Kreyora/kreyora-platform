# Documentation and Checkpoint Rules

Applies to: documentation files, ADRs, milestone files, checkpoint reports, and context files.

## History integrity

- Do not rewrite approved history silently.
- Corrections must be appended or tracked, not overwritten.
- When updating status tables, preserve the record of prior states.

## Evidence standards

- Use exact evidence: file paths, commit hashes, test output, command results.
- Mark assumptions explicitly with `[ASSUMPTION]`.
- Mark unresolved dependencies explicitly with `[UNRESOLVED]`.
- Do not fabricate test results, provider capabilities, or compliance evidence.

## ADR requirements

- An ADR is required for any change to locked technology, state machine, tenant boundary, public API convention, provider, payment policy, or deployment topology.
- ADRs must follow `docs/decisions/ADR_TEMPLATE.md`.
- ADRs include context, decision, alternatives, consequences, migration impact, owner, and validation evidence.

## Checkpoint requirements

- A checkpoint report is required for every completed prompt step.
- Checkpoints must follow `artifacts/checkpoints/CHECKPOINT_TEMPLATE.md`.
- Checkpoints include scope, files, verification evidence, security review, and next-permitted-prompt confirmation.
- The checkpoint must explicitly state that the next prompt was not started.

## Status vocabulary

Use only these values for milestone/step status:

| Status | Meaning |
|---|---|
| `NOT STARTED` | No implementation work accepted |
| `IN PROGRESS` | Current prompt is being implemented |
| `REVIEW` | Work complete, waiting for verification |
| `APPROVED` | Evidence satisfies the prompt; may be built upon |
| `CHANGES REQUESTED` | Corrective work required in the same step |
| `BLOCKED` | External dependency or decision prevents completion |
