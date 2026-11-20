---
name: handoff
description: "Compact the current conversation into a handoff document for another agent to continue the work when context windows get large."
argument-hint: "What should the next session focus on?"
---

# Handoff

Create a handoff markdown file that summarizes the current conversation so a fresh agent can continue the work.

Save the file in the workspace under `docs/plans`, not the temporary directory.

Always create a new `.md` file in `docs/plans`, using a timestamped or otherwise unique filename so existing handoff files are not overwritten.

After writing the file, respond with the file path and a one-line summary only. Do not print the full handoff content in chat unless file creation fails.

Tailor the document to the user's argument, if provided, as the focus for the next session.

Include these sections:

- Current objective
- What is already known
- Work completed so far
- Open questions or blockers
- Recommended next steps
- Files, plans, ADRs, issues, commits, or diffs to reference by path or URL instead of duplicating
- Suggested skills the next agent should invoke

When handing off implementation work, also preserve:

- Approved scope and constraints, including any explicitly approved scope changes; keep proposed work separate from approved work.
- The existing plan and checklist locations, item identifiers, verified progress, and remaining or blocked items. Reference the existing checklist rather than creating a competing copy or marking unverified work complete.
- For unit-test work, the current TDD phase and relevant stub/test names, observed red/green commands and results, characterization coverage, and any unexecuted or blocked checks. Planning placeholders are not evidence of TDD execution.
- Required next validation and unmet gates. Do not infer missing results or direct the next agent to expand scope without approval.

Keep the handoff concise, factual, and easy for a new agent to resume from.
Redact secrets, API keys, passwords, tokens, and personally identifiable information.
Do not repeat content already captured in other artifacts unless a short reference is enough.