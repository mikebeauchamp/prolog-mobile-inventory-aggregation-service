---
applyTo: "**"
---

# MCP Server Usage Reference

> Platform routing rules and tool usage guidance shared across all agents.
> These rules are authoritative — do not fall back to portals, REST calls, or guessed platform state.
>
> `activate_*` bundle names are non-authoritative unless they are actually callable in the current session.

---

## Platform Routing Rules

| Platform | Use | Never substitute |
|---|---|---|
| **GitHub** | `github/*` MCP tools only | Web UI, ad-hoc REST, `web/githubRepo` |
| Playwright | playwright/* | Local terminal runner or browser execution used to bypass restrictions |
| Context7 | context7/* | Guessed current library APIs |
| Microsoft Learn | microsoft-learn/* | Guessed Microsoft APIs or configuration |

If a required MCP server is unavailable, **state the blocker explicitly**. Do not fall back.

### Wiki Capability Limits

No terminal exception exists in this strict MCP-only profile. If declared GitHub tools cannot perform a wiki operation, report it as unavailable. Do not substitute browser automation, terminal Git, or ad-hoc REST calls.

## Shared Access And Discovery

- Allowed declarations are playwright/*, context7/*, microsoft-learn/*, and existing role-scoped github/ tools. These names must match the target VS Code MCP registration IDs; generated internal call names are not registration IDs. Report mismatches instead of claiming access or broadening provider scope.
- All agents, especially planning, coding, and review agents, use the routing below. Check live callable tools and schemas. Declarations do not install, enable, authenticate, or establish connectivity.
- If tools are deferred and host-managed discovery is exposed, search for the specific operation before invocation. Discovery is not an additional provider in agent tool lists. If unavailable, use already-exposed tools only and report exact missing operations.
- Except for the Coding Agent, which has `read`, `edit`, `search`, `execute`, and `todo`, and the Planning Agent, which has `read`, `search`, and `edit` limited to plan and checklist files, local reads, edits, terminal tests, memory, tasks, and automatic delegation are unavailable. Do not bypass these limits through browser scripts, filesystem access, arbitrary execution, or unapproved GitHub writes. Return drafts and report blocked steps.
- Preserve secret handling, approval gates, read-only review scope, and owned artifact cleanup. If safe storage or cleanup is unavailable, report the blocker before creating artifacts.

## Context7 Documentation

- During planning, coding, and reviews, use Context7 when correctness depends on current library or framework API syntax, versions, configuration, migration, or recommended usage. Verify installed versions from supplied or GitHub-readable evidence; do not assume latest is installed.
- Resolve the library with resolve-library-id, then retrieve focused documentation with query-docs. Reuse resolved IDs. If unavailable, state the gap; use official Microsoft Learn documentation for Microsoft technologies where appropriate, and never invent API signatures.
- Discovery queries: Resolve library ID Context7 resolve-library-id; Query docs retrieve documentation Context7 query-docs.

## Microsoft Learn Documentation

- During planning, coding, and reviews of C#, .NET, ASP.NET Core, Azure, and other Microsoft technologies, verify version-sensitive recommendations and contracts against official documentation.
- Start with microsoft_docs_search; use microsoft_code_sample_search for official samples and microsoft_docs_fetch for complete prerequisites or detailed configuration. Cite sources and distinguish documentation from observed runtime behavior.
- Discovery queries: Search official Microsoft documentation microsoft_docs_search; Search official Microsoft code samples microsoft_code_sample_search; Fetch complete Microsoft documentation microsoft_docs_fetch.

## Playwright Browser Evidence

- Use Playwright MCP to investigate UI acceptance criteria, reproduce browser behavior, and verify visible outcomes during coding and reviews. Planning uses observations to inform requirements, not to claim a future fix is verified.
- Use normal navigation and user interactions with a verified URL. Respect read-only scope and approval gates. Do not trigger production mutations, inspect secrets, or use browser execution as a terminal or filesystem substitute.
- Verify navigation, snapshot, interaction, and screenshot operations and schemas. Inspect returned browser evidence before claiming visible success. Browser assertions do not prove local unit suites or builds ran.
- Discovery queries: Playwright browser navigate browser_navigate; Playwright browser snapshot browser_snapshot; Playwright browser take screenshot browser_take_screenshot.

---

## GitHub MCP Server

### tool_search exact queries

The configured GitHub MCP provider is `io.github.github/github-mcp-server`.
The `github/...` examples below are operation shorthand; verify the live provider and schema.
Do not substitute tools from the GitHub Pull Requests extension, including
`github-pull-request_create_pull_request` or its `create_pull_request` alias with a nested `repo` input.

Use these exact queries when the required GitHub MCP tools are not already exposed in the session:

| Operation | Exact tool_search query |
|---|---|
| Get current identity and permissions | `GitHub get current identity and permissions get_me` |
| Read a pull request by number | `Read a pull request by number pull_request_read` |
| Search pull requests | `Search GitHub pull requests search_pull_requests` |
| Get file contents from a repository | `Get file contents from GitHub repository get_file_contents` |
| Read a GitHub issue by number | `Read a GitHub issue by number issue_read` |
| List pull requests in a repository | `List pull requests in a GitHub repository list_pull_requests` |
| Create a pull request | `GitHub MCP Server create a pull request create_pull_request` |
| Update a pull request | `GitHub MCP Server update a pull request update_pull_request` |

Use these queries only when `tool_search` is callable. Discovery results still require provider and
schema inspection; a matching name alone is insufficient. If discovery is unavailable or does not
return the required GitHub MCP operation, report that exact operation as blocked.
For PR workflows, verify the applicable write operation and PR read-back before drafting.
Do not call a write tool merely to test availability.

After an approved PR write, require the returned number and URL and verify the resulting PR with
`pull_request_read`. If a write fails or its outcome is uncertain, check for a matching existing PR
before retrying. An extension callback error does not establish a GitHub MCP configuration fault;
never fabricate a callback URL.

### Read operations

```
# Get identity and permissions context
github/get_me

# Read PRs
github/pull_request_read(owner, repo, pullNumber)
github/list_pull_requests(owner, repo)
github/search_pull_requests(query)

# Read issues
github/issue_read(owner, repo, issueNumber)
github/list_issues(owner, repo)
github/search_issues(query)

# Read commits and code
github/list_commits(owner, repo)
github/get_commit(owner, repo, sha)
github/search_code(query)
github/get_file_contents(owner, repo, path)

# Read branches, tags, releases
github/list_branches(owner, repo)
github/list_tags(owner, repo)
github/list_releases(owner, repo)
```

### Write operations (require explicit user approval)

```
# PR management
github/create_pull_request(...)
github/update_pull_request(...)
github/merge_pull_request(...)         ← REQUIRES EXPLICIT APPROVAL
github/pull_request_review_write(...)  ← REQUIRES EXPLICIT APPROVAL

# Comments
github/add_issue_comment(...)
github/add_comment_to_pending_review(...)

# Branches and files
github/create_branch(...)
github/push_files(...)                 ← REQUIRES EXPLICIT APPROVAL
github/create_or_update_file(...)
github/delete_file(...)                ← REQUIRES EXPLICIT APPROVAL
```

### PR search examples

```
# PRs that touched a specific file (<repository>)
search_pull_requests: repo:<github-owner>/<repository> [filename]

# PRs that touched a specific file (MCP repo)
search_pull_requests: repo:<github-owner>/<repository> [filename]

# Recent merged PRs
search_pull_requests: repo:<github-owner>/<repository> is:merged sort:updated
```

### What to extract from PRs and commits
- PR title, description, linked GitHub issue IDs
- Files changed and diff
- Review comments (may explain design decisions)
- CI/check status
- Commit messages (often reference work item numbers)
- Who made changes and when
- Cross-repo changes that happened together

---

