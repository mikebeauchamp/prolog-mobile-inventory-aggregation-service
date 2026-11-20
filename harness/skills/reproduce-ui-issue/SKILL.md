---
name: reproduce-ui-issue
description: "Reproduce and investigate UI issues with Playwright using normal user interactions, controlled asynchronous responses, and inspected screenshots. Use when: reproduce UI bug, inspect screenshot, dialog flicker, blinking screen, layout shift, loading placeholders, visual regression, focus jump, inconsistent browser behavior, or verify a UI fix. Separates live evidence from mocked evidence and visual quality from passing assertions."
---

# Reproduce a UI Issue

## Scope and Safety

- Investigation is read-only unless the user requests implementation. Obtain approval for new design choices under the workspace approval gate.
- Use an existing supported desktop viewport for UI investigations. Do not add mobile scope unless requested. A cropped screenshot is not evidence of the full browser viewport.
- Never inspect authentication values, storage state, cookies, headers containing secrets, or protected files. If authentication is required, use the project approved authentication flow; if unavailable, report the blocker without inspecting secrets.
- Synthetic authentication demonstrates only the synthetic UI path, not real permissions, tenant state, or backend behavior. Mock mutations must never reach the live backend.
- Route screenshots, traces, and logs into a unique owned OS-temp directory. Record its absolute path before creating evidence. Follow the shared cleanup policy, including runner-created artifacts whose output path cannot be redirected.

## 1. Establish the Reproduction Contract

Record these facts in the conversation, not a new report file unless requested:

| Field | Required information |
|---|---|
| Report | User-visible symptom, screenshot text, expected result |
| Runtime | Verified URL and port, served application build when known, development or production mode, unknowns |
| Browser | Browser engine, actual viewport, zoom if known, theme, relevant locale |
| State | Role, synthetic or live data, current screen, prerequisites |
| Trigger | Exact normal clicks and the async response involved |
| Evidence | Observable result that would reproduce or disprove the symptom |

Check reachable local endpoints; do not assume port 3000 or start a second server unnecessarily. If the reported app is served on port 8000, inspect that app first. Do not equate local source changes with the bundle currently served by a container.

Read the controlling component and one neighboring test/caller. Form one falsifiable hypothesis and choose a cheap discriminating check. Do not map the entire UI before reproducing the issue.

## 2. Verify Tools and Isolate the Browser

Use the declared Playwright MCP server and verify its live operations using [MCP routing and discovery](../../instructions/mcp-usage.instructions.md). This profile has no local file or terminal tools: do not require a local runner, start a server, or claim local suites ran. Request a verified reachable URL or report the blocker. Preserve user-owned browser sessions and isolate investigation when supported.

Use the real application route and normal user clicks. Prefer accessible roles and names, then stable test IDs. Scope actions to the active dialog. Do not substitute direct component mounting or injected application state for the primary reproduction; if necessary as a diagnostic fallback, label it as such and explain its limits.


## 3. Control Timing Without Guessing

- Register response interception and request counters before the triggering click.
- Hold the specific response with a promise gate, not a fixed sleep or guessed network delay.
- Wait for a meaningful loading-state assertion, capture that state, then release the response and assert readiness.
- Keep route closures, gates, captures, release, and cleanup in one supported Playwright execution unless persistence has been verified. Do not assume functions or Node-side state survive separate MCP calls. Report deterministic timing checks as blocked if interception is unavailable.
- Use `try`/`finally` to release outstanding gates, disconnect observers, and remove owned routes. Do not let a failed assertion strand a request.
- For flicker, observe the natural opening/transition first. Animation-disabled screenshots or forced animation completion are useful for settled geometry only; they cannot disprove an animation-related blink.
- Do not infer request duplication from a screenshot or React development mode. Count initiated, cancelled, and completed requests when accessible. Explain which count was observed. Compare development and production only when needed to distinguish a hypothesis.

## 4. Capture Behavior and Appearance Separately

Capture a normal viewport screenshot of the loading state and another of the ready state. Add a scoped dialog screenshot when helpful. Use explicit absolute paths in the owned temporary directory. Include enough page context to see overlays and positioning.

Inspect screenshot images returned by Playwright when available. If only a file path is returned and no allowed tool can inspect it, report visual inspection as blocked. Taking a screenshot or passing a locator assertion is not visual review. Compare with the user screenshot before claiming reproduction.

For a resizing or apparently reopening dialog, collect:

- Bounding boxes before and after the response, including width, height, x, and y deltas.
- DOM identity of the surface and a removal observer to distinguish content replacement from unmount/remount. Disconnect the observer afterward.
- Number of open dialogs and relevant preview requests. These are separate observations.
- Focused control before and after; primary-action enabled/disabled states.
- Loading, ready, error, and retry visibility and relevant accessible busy/live status. DOM attributes alone do not prove a screen-reader announcement was spoken.

Visually inspect hierarchy, visible loading feedback, placeholder prominence, text wrapping, spacing, clipping, contrast, backdrop changes, and whether the content looks unfinished. Describe subjective judgments as visual assessments. Zero layout shift is not evidence of good UX.

## 5. Repeat and Exercise the Relevant Failure

Repeat the normal open -> loading -> ready -> cancel path at least twice with the same seed and viewport when safe. Count each cycle separately. Confirm closing cancels or safely ignores the outstanding request when that is the reported behavior.

For asynchronous issues, also reproduce the relevant failure/retry path with deterministic synthetic responses. Preserve existing behavior and do not invent new timeouts, retry policies, or fallback UX. If a necessary state cannot be exercised safely, report it as unverified rather than claiming broad coverage.

Use the nearest existing Playwright spec for a durable regression only when implementation or test edits are authorized. Do not create a new harness, framework, or task entry merely to capture a one-off reproduction. Keep per-test output live and readable for any test run.

## 6. Report, Then Clean Up

Report these categories separately, omitting empty ones:

| Category | Content |
|---|---|
| Observed | Exact runtime, fixture scope, click path, repetitions, numeric measurements, and inspected visual findings |
| Inferred | Explanation supported by observations but not directly established |
| Unverified | Live timing, real tenant state, build identity, screen-reader speech, or other missing evidence |
| Proposed | Smallest corrective options and tradeoffs; no redesign without approval |

Do not call an issue fixed because only the loading geometry test passed. When a fix is authorized, repeat the same original reproduction and inspect new screenshots after the final edit, then run focused regressions.

Before final handoff, record necessary sanitized findings, stop only servers and browser contexts owned by this run, release routes/gates, and delete only exact verified owned artifact paths. Reject reparse points; verify absence after deletion. Do not delete pre-existing reports or caches. Retain raw evidence only when explicitly requested, and report exact retained paths. Do not leave final links pointing at deleted screenshots.

