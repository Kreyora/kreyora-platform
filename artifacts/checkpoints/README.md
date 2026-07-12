# Checkpoint Reports

This directory stores checkpoint reports for each completed milestone prompt step.

## Naming convention

`M<NN>-S<NN>.md` — where `<NN>` is the zero-padded milestone and step number.

Examples:
- `M01-S01.md` — Milestone 01, Step 01
- `M02-S03.md` — Milestone 02, Step 03

## Special checkpoints

- `BOOTSTRAP_CHECKPOINT.md` — Documents the initial context-system setup.

## Template

Use `CHECKPOINT_TEMPLATE.md` in this directory for all new checkpoint reports.

## Rules

- Every prompt step produces exactly one checkpoint report.
- The checkpoint must be reviewed and marked `APPROVED`, `CHANGES REQUESTED`, or `BLOCKED`.
- Do not modify approved checkpoints; create follow-up entries instead.
- See `.claude/rules/documentation-and-checkpoints.md` for full documentation standards.
