---
applyTo: "**/*.test.{ts,tsx},**/__tests__/**,**/tests/**/*.py,**/test_*.py"
---

# Testing Standards

> Angle-bracket markers are descriptive placeholders, not actual filenames. Replace them with verified target-project paths before adapting an example or command.
>
> Applies automatically to all test files. For non-test tasks that still need the shared testing workflow, read this file directly: `.\.github\instructions\testing-standards.instructions.md`

---

## Unit-Test TDD And Planning Handoff

- The Planning Agent creates acceptance-criterion-linked unit-test stubs describing the behavior and expected outcome. These may remain explicitly skipped planning placeholders; they are not completed tests or evidence of TDD execution. Planning-only work does not require filling in assertions, changing production behavior, or claiming green tests or measured coverage.
- The Coding Agent completes and enables those same stubs with executable setup, actions, and meaningful assertions. Read existing tests and fixtures first; reuse them rather than creating duplicate tests.
- For each new or changed unit-tested behavior, follow red-green-refactor:
  1. Red: write or complete the relevant unit test before changing production behavior. Run it and verify that it fails for the expected missing or incorrect behavior.
  2. Green: make the smallest correct production change within the approved scope, then rerun the same test to verify it passes.
  3. Refactor: improve only the touched implementation when needed, stay within scope, and rerun affected tests.
- Skips, TODOs, placeholder exceptions, and unrelated import, fixture, mock, or environment failures do not count as red. Diagnose setup failures before claiming the expected behavioral failure was observed.
- Tests for already-working behavior may pass immediately; report them as characterization coverage. Do not break production to manufacture red. Test-only requests do not authorize production changes.
- When implementing a planning handoff, map changes and tests to its approved checklist and acceptance criteria. Make the smallest correct, maintainable changes needed; obtain approval before expanding scope. If the checklist is missing, ambiguous, or conflicts with the plan, ask for clarification rather than inventing requirements.
- Keep required coverage thresholds and regression gates. Additional tests must stay within the approved scope; unrelated failures or coverage gaps do not authorize unrelated fixes. Report unmet gates and blockers explicitly instead of claiming full completion or silently lowering thresholds or widening exclusions.
- Record observed red/green commands and results, or explicitly identify characterization coverage or blocked execution. Mark checklist items complete only after their required checks succeed; skipped planning stubs and unverified checks remain incomplete.

## Coverage Targets

**Coverage floor:** 85% on applicable statements, branches, functions, and lines; retain higher existing gates. The 85-90% target does not mandate a framework or application layout.

These are floors, not ceilings. Do not over-engineer tests purely to chase 100%.

---

## Capability Limits

- Use existing target-project test frameworks and conventions, not prescribed application fixtures or layouts.
- This profile cannot edit local test files or execute local suites. Return scenarios or draft stubs and report execution as blocked. Never claim red, green, coverage, or regression results without observed evidence.
- Required regression and coverage gates remain unmet until executed checks supply evidence. Do not lower gates or expand scope merely to report completion.
