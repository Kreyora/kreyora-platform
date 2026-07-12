# Session Start — Recovery Prompt

Use this prompt at the beginning of any new Claude session working on Kreyora implementation.

---

## Recovery procedure

You are resuming work on **Kreyora**, a Nepal-focused multi-tenant social-commerce operating system. Before making any changes, recover your context:

1. **Read current state:** Open and read `docs/context/CURRENT_WORK.md`. Note the active milestone, step, status, branch, last checkpoint, blockers, and next permitted action.

2. **Read the context manifest:** Open `docs/context/CONTEXT_MANIFEST.md`. Identify which reading path applies to your current task (frontend, backend, integration, AI, deployment, security, or review).

3. **Read the milestone index:** Open `design_files/project_a_milestones/00_MASTER_INDEX.md`. Confirm the milestone sequence and source hierarchy.

4. **Read the active milestone:** Open only the milestone file indicated by `CURRENT_WORK.md`. Read the prompt for the current step and its acceptance criteria.

5. **Read relevant plan sections:** Open `design_files/plan.md` and read the sections relevant to your task (Sections 10–11 for architecture; specific subsections as indicated by the context manifest).

6. **Read accepted ADRs:** Open `docs/decisions/ADR_INDEX.md`. Read any accepted ADRs that affect the current work.

7. **Read the last checkpoint:** Open the most recent checkpoint file from `artifacts/checkpoints/`. Understand what was last completed and approved.

8. **Inspect Git state:** Run `git status` and `git log --oneline -10`. Check for uncommitted changes, branch state, and recent commits.

9. **Summarize your recovered state** before making any edits. Include:
   - Current milestone and step
   - Status and what has been completed
   - What the current step requires
   - Known blockers or dependencies
   - The next permitted action
   - The next prohibited action

10. **Wait for human direction** before making edits. If the next permitted action is "plan mode," propose the plan and wait for approval. If it is "implement," confirm the approved plan exists before coding.

---

## Rules reminder

- Execute exactly one prompt step at a time.
- Never start the next prompt automatically.
- Never commit, push, create a PR, deploy, or contact an external service without explicit authorization.
- Finish every step with a checkpoint report.
- Update `CURRENT_WORK.md` after every approved state change.
