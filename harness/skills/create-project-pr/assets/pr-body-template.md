# PR Body Template

This is the reusable pull request body template for the harness.
It defines a reusable change and validation summary. All `{PLACEHOLDER}` values must be derived from
verified change evidence, GitHub issue data, and inspected file contents — never invented.

---

## [#{ISSUE_NUMBER}]({ISSUE_URL}) — {issue Title}

{One paragraph. Describe what the bug or feature was (include issue type, priority, severity, and
target version when stated in the GitHub issue). Then summarize what this branch implements at a high level
across all layers.}

---

### {Layer Name} — {Descriptive Section Title}

| Area | What Changed | Why |
|------|--------------|-----|
| **`{path/to/changed/file}`** | {Specific changes: symbols added, functions modified, logic removed, or new module introduced.} | {Rationale: what this enables, prevents, enforces, or satisfies — tied to the AC or bug cause.} |

<!--
Repeat the layer section (### heading + table) for each logical grouping of changes:
Backend, Frontend, MCP Server, CI/CD, Tests, etc.
Only include a section if that layer has changes. Omit empty sections.
-->

### Testing

| Layer | What Was Added | Purpose |
|-------|----------------|---------|
| Unit | {test files, functions added, and coverage delta} | {what these tests assert} |
| Integration | {test files and scope} | {what they verify end-to-end within the service} |
| E2E | {Playwright spec files} | {user-visible behavior they cover} |
| Prompt-Eval | {eval files, if applicable — omit row if no prompt-eval tests were added} | {what adversarial or regression inputs they guard against} |

<!--
Omit the Testing section entirely only if no tests were changed or added.
Omit individual rows (Unit, Integration, E2E, Prompt-Eval) for layers that have no new tests.
-->

---

Resolve issue links through the shared GitHub Issue Context rules. Use a verified repository-qualified issue URL and title; if no issue is linked, use a verified change-title heading instead. Do not add closing keywords or invent metadata.

## Formatting Rules (do not include in the final PR body)

- Use an em-dash (—) between the GitHub anchor and the issue title in the heading.
- `Area` column values use bold inline-code: `**\`path/to/file\`**`
- `What Changed` column: describe specific symbols, functions, or logic; never vague ("updated code")
- `Why` column: tie each change to an acceptance criterion, bug cause, or architectural rationale
- Do not add a "Files Changed" section — the table rows cover individual files
- Do not add a "Validation" section — the Testing table covers verification evidence
- Do not include HTML comments in the final PR body
