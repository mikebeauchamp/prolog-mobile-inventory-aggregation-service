---
name: find-unmerged-prs
description: "Find GitHub PRs merged into a source branch that have not been cherry-picked into a target branch, or vice versa, for the verified target repository. Uses GitHub API exclusively — no git log parsing. Use when: what PRs haven't been cherry-picked, what is missing from release, what dev PRs are not in release, gap analysis between branches, find missing cherry-picks, which PRs need to be backported."
argument-hint: "source-branch target-branch [repo]"
---

# Find Unmerged PRs (Branch Gap Analysis)

Determines which PRs merged into a **source branch** have no corresponding cherry-pick in a
**target branch**, using the GitHub API only. No git log, no grep of commit messages, no guessing.

## When to Use

- "What is missing from the release branch?"
- "What dev PRs are not in release?"
- The reverse: "What release PRs haven't been brought back to dev?"

## Core Principle — Why GitHub API, Not Git Log

Git commit SHAs change on cherry-pick. A cherry-pick of PR #463 creates a new commit on the target
branch with a different SHA. This means `git log branchA --not branchB` will show the original
merge commit as "missing" even when the content is already on the target branch via a cherry-pick.

The only reliable source of truth is the GitHub PR metadata itself:

- Do not assume a PR-title convention. Require an explicitly confirmed correspondence convention; otherwise report matching as unverified.
- A cherry-pick PR always targets the destination branch as its `base`.
- The GitHub API returns the canonical `base.ref` and full title for every PR.

---

## Procedure

### Step 1 — Resolve inputs

Accept any of these input forms:

If either branch is ambiguous, ask the user before proceeding.

### Step 2 — Fetch all merged PRs for the source branch

Call `github/list_pull_requests` with:
- `base` = source branch
- `state` = `closed`
- `perPage` = 100

Filter the response to only entries where `merged_at` is non-null. These are the confirmed-merged
PRs. Collect: `number`, `title`, `merged_at`, `head.ref`.

If the response returns exactly 100 results, fetch page 2 (`page=2`) and continue until a page
returns fewer than 100 results. This ensures complete coverage.

### Step 3 — Fetch all merged PRs for the target branch

Call `github/list_pull_requests` with:
- `base` = target branch
- `state` = `closed`
- `perPage` = 100

Filter to `merged_at` non-null. Collect: `number`, `title`, `merged_at`.

Paginate the same way as Step 2.

### Step 4 — Extract cherry-pick back-references from target PRs

For every merged target-branch PR, scan the title for the pattern `\(#(\d+)\)`.

This pattern is the canonical back-reference to the source PR. The number inside the parentheses
is the source PR number that was cherry-picked.

Build a set: **cherry-picked source PR numbers** = all numbers extracted this way.

### Step 5 — Identify gaps (source PRs not cherry-picked to target)

A source PR is a **gap** if ALL of the following are true:

1. Its `number` is NOT in the cherry-picked source PR numbers set from Step 4.
2. Its title does NOT contain `(#NNN)` where `NNN` matches any **target branch** PR number
   (this identifies cherry-picks FROM the target TO the source — their content is already in target).
3. Its title does NOT contain `Cherrypick`, `Cherry Pick`, `Cherry-Pick`, `DEV-Cherrypick`,
   `Dev-Cherrypick`, or `Dev Cherrypick` (any capitalisation) — these are cherry-pick PRs
   themselves, not original feature work.

Source PRs that fail criterion 2 or 3 are labelled **"cherry-pick from target to source"** — their
content originated on the target branch and the source PR merely brought it over. They are NOT gaps.

### Step 6 — Present results

Output two tables per repository:

#### Table A — Source PRs NOT in target (gaps)

| PR | Title | Merged to source |
|---|---|---|
| #NNN | title | YYYY-MM-DD |

If the table is empty, say: "All merged `{source}` PRs have been cherry-picked into `{target}`."

#### Table B — Source PRs confirmed already in target

| Source PR | Title | Via target PR |
|---|---|---|
| #NNN | title | #NNN (cherry-pick PR on target) |

List only entries where there is a confirmed `(#SourcePR)` reference in a target PR title.

---

## Edge Cases and Rules

### Squash-merged cherry-picks without `(#NNN)` title conventions

Some cherry-picks in these repositories were squash-merged without appending `(#OriginalPR)` to
the title. These cannot be detected by title scan alone. When a source PR appears to be a gap
but has a close date match or identical # to a target PR, note it as **UNCERTAIN** rather than
asserting it is missing. Do not guess — flag it for human review.

### DEV-Cherrypick naming (cherry-picks FROM target TO source)

PRs on the source branch whose titles end with `DEV-Cherrypick`, `Dev-Cherrypick`, or
`Dev Cherrypick` originated on the target branch and were cherry-picked to the source. Their
content is already in the target branch. They are **not gaps**.

Pattern: if a source PR title contains any case-insensitive variant of `cherrypick` or
`cherry pick` or `cherry-pick`, check whether it also contains `(#NNN)` referencing a target PR.
If it does, it is definitively not a gap. If it contains a cherrypick keyword but no reference,
mark it as **UNCERTAIN (cherry-pick origin)**.

### PRs that predate the target branch

If a source PR was merged before the target branch was created (i.e., the target branch was cut
from a commit that includes that source PR), it is already in the target naturally. These will
typically not appear as gaps because the target branch content includes them. Do not flag them.

### Pagination completeness check

After fetching, verify: if any fetched page returned exactly 100 results, explicitly note in the
output that page 2+ was fetched and state the total PR count. This makes it clear the analysis
is complete.

---

## Output Format

```
## Branch Gap Analysis — {repo}

**Source branch:** {source}  ({N} merged PRs found)
**Target branch:** {target}  ({M} merged PRs found, {K} reference source PRs by (#NNN))

### PRs merged into `{source}` NOT found in `{target}`

| PR | Title | Merged |
|---|---|---|
| ... | ... | ... |

### PRs confirmed cherry-picked into `{target}`

| Source PR | Title | Via |
|---|---|---|
| ... | ... | ... |

### Uncertain (no (#NNN) reference, cannot confirm via title scan alone)

| PR | Title | Reason |
|---|---|---|
| ... | ... | Same # as target PR #NNN but no back-reference |
```

---

## What NOT to Do

- **Never use `git log`** to determine what is or is not cherry-picked. SHAs differ after cherry-pick.
- **Never search commit messages** for # numbers as the sole confirmation. Commit messages are
  not authoritative for determining cherry-pick status — the PR title `(#NNN)` convention is.
- **Never assume** a PR is missing or present. Every assertion must be traceable to a GitHub API
  response field (`merged_at`, `title`, `base.ref`).
- **Never fabricate** PR numbers, titles, or dates.
- **Do not stop** when a result is empty — an empty gap table is a valid and complete result.
