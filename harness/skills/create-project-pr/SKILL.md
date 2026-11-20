---
name: create-project-pr
description: "Create or update a GitHub pull request for the verified target repository. Produces PR bodies following the included PR body template: linked GitHub issue heading, one-paragraph summary with issue metadata, per-layer tables (Area / What Changed / Why), and a Testing table (Layer / What Was Added / Purpose). Use when: create PR, open pull request, submit for review, update PR description, publish branch, PR with GitHub link, open project PR."
argument-hint: "Optionally specify a target base branch, repository, or whether to create as a draft"
---

# Create Pull Request

## When to Use

- The current branch has commits ready to publish and you want to open or update a PR
- You need a PR body that follows the established template pattern
- The branch name contains an `#NNNNN` reference and you want the PR linked to the GitHub issue

## Template Reference

The canonical PR body template lives at [./assets/pr-body-template.md](./assets/pr-body-template.md).
Load it before writing the PR body. Never deviate from its structure.

## Platform Rules

- **GitHub operations → GitHub MCP Server tools only.** No web UI fallbacks.
- **GitHub operations → GitHub MCP Server tools only.**
- Never create a PR without explicit user approval in the current conversation.
- Never guess at code intent. Every description line must be traceable to committed diffs or GitHub issue data.

## Procedure

### 0. Verify the GitHub MCP provider and capabilities

- Use the configured `io.github.github/github-mcp-server` provider. Tool names in this skill such as `github/create_pull_request` are operation shorthand, not proof of provider identity or runtime availability.
- Before preparing a draft, verify callable existing-PR listing or search and PR read-back tools. After checking for an existing PR, verify the applicable creation or update operation.
- For missing operations, use the exact recovery queries in [the shared MCP reference](../../instructions/mcp-usage.instructions.md) if `tool_search` is callable. If discovery is unavailable or unsuccessful, stop before drafting and report the exact missing operation.
- Inspect the actual provider and input schema. Do not substitute the GitHub Pull Requests extension's `github-pull-request_create_pull_request`, which may appear as `create_pull_request` and accepts a nested `repo` object.
- A callback error from that extension is not evidence that GitHub MCP needs a callback configuration. Never invent a callback URL or change authentication, MCP configuration, or installed extensions without verified cause and approval.

### 1. Identify the repository

- If the user names a repository, use it.
- Otherwise ask for the repository and comparison scope; do not assume a supported repository list or branch convention.

### 2. Verify accessible branch evidence

- Resolve head and base from explicit user instructions and current GitHub evidence. Ask when ambiguous; do not choose a default branch.
- This profile cannot inspect a local worktree or execute Git. Report inaccessible local state rather than claiming it is clean or published.

### 3. Check for an existing PR

- Use `github/list_pull_requests` or `github/search_pull_requests` to find any open PR from the same head branch.
- If one exists, update its title and body rather than creating a duplicate.

### 4. Gather evidence for the PR body

Use current GitHub PR, commit, and file evidence available through declared tools, or explicitly supplied change evidence. Report missing comparison evidence rather than running local commands.

Read key changed files when needed to understand the rationale — do not paraphrase filenames as content.

### 5. Verify linked GitHub issues

Resolve issue links through the shared GitHub Issue Context rules. Use a verified repository-qualified issue URL and title; if no issue is linked, use a verified change-title heading instead. Do not add closing keywords or invent metadata.

### 6. Load the template

Read [./assets/pr-body-template.md](./assets/pr-body-template.md). Use it as the exact structural guide.
Fill in each `{PLACEHOLDER}` from verified evidence. Remove all HTML comments and formatting-rule notes
before using the output as the PR body.

### 7. Write the PR body

Follow the template structure exactly:
- **Heading:** `## [#{ID}]({ISSUE_URL}) — {Verified issue Title}`
- **Summary paragraph:** What the bug/feature was (type, priority, severity, target version) + what this branch implements.
- **`---` separator**
- **Per-layer sections:** `### {Layer} — {Section Title}` with `| Area | What Changed | Why |` table. Include Backend, Frontend, MCP Server, CI/CD sections only for layers that have changes.
- **Testing section:** `| Layer | What Was Added | Purpose |` table. Include only layers with new or modified tests. Omit the section entirely if no tests changed.

**Table formatting rules:**
- `Area` column: `**\`path/to/file\`**` (bold inline-code)
- `What Changed`: specific symbols, functions, or logic — never vague summaries
- `Why`: tie to an AC, bug cause, or architectural rationale

### 8. Build the PR title

Use the user's supplied title or present a draft based on the verified change scope for approval. Do not require a repository-specific branch naming convention.

### 9. Request approval, then create or update

Present the draft title and body to the user. Wait for explicit approval before calling any write tool.

Then:
- **New PR:** the verified GitHub MCP `create_pull_request` operation, using its actual schema for repository, `title`, `head`, `base`, and `body`.
- **Existing PR:** the verified GitHub MCP `update_pull_request` operation, using its actual schema for repository, PR number, `title`, and `body`.

Require a returned PR number and URL, then read the PR through GitHub MCP and verify the repository,
head, base, title, and body before reporting creation or update as successful.
If a write fails or its outcome is uncertain, check for an existing PR from the same head before
retrying. Read back any match; do not assume the failed response means no PR was created.

### 11. Report the result

Output the PR URL, whether it was created or updated, the base branch, and the GitHub issue linked (if any).

## Completion Self-Check

- [ ] Repository, head, and base verified from accessible evidence, not inferred from editor metadata
- [ ] Inaccessible local worktree state reported as unverified
- [ ] Base branch determined from verified evidence, not assumed
- [ ] Existing open PR checked before creating a new one
- [ ] Applicable write and read-back operations verified as callable GitHub MCP tools before drafting
- [ ] No GitHub Pull Requests extension tool substituted for GitHub MCP
- [ ] All PR body content derived from verified change and requirement evidence
- [ ] Template structure followed — no Files Changed or Validation sections added
- [ ] User approval received before calling any write tool
- [ ] GitHub issue linked after PR creation
- [ ] PR URL in the final response
- [ ] PR state verified through a subsequent GitHub MCP read
