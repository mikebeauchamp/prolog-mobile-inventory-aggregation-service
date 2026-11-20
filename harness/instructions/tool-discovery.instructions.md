---
applyTo: "**"
---

# Tool Discovery Contract

- Allowed providers only: Playwright, Context7, Microsoft Learn, and GitHub. Follow [MCP routing and discovery](mcp-usage.instructions.md). Local tools are intentionally excluded except for the Coding Agent, which has `read`, `edit`, `search`, `execute`, and `todo`, and the Planning Agent, which has `read`, `search`, and `edit` limited to plan and checklist files.

- Treat agent tool lists as intent, not proof of runtime availability.
- Prefer the live callable tool surface when the required tool is already exposed in the current session.
- If a required tool is deferred and host-managed discovery is callable, use the exact recovery query before invocation. Discovery is not an additional provider declaration. If discovery is absent, use already-exposed tools only and report exact missing operations.
- Do not rely on `activate_*` bundle names unless those bundle tools are callable in the current session.
- If discovery fails, report the exact missing operation rather than saying the whole platform is unavailable.


