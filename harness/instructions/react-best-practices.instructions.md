---
description: "Use when planning, writing, reviewing, or testing React components and Hooks, state updates, Effects, asynchronous UI synchronization, or render purity. Includes official sources."
applyTo: "**/*.jsx,**/*.tsx"
---

# React Practices

## Scope and Evidence

- Check the installed React version, framework, compiler configuration, lint rules, component library, and test runner before proposing APIs or tooling changes. Do not silently add dependencies or assume the newest APIs are available.
- Apply this file explicitly to React Hooks in `.js` or `.ts` files and to planning or read-only reviews; file matching alone does not cover every React task.
- The official React sources below were consulted on 2026-11-20. Recheck version-specific behavior when a change depends on it.

## Rendering and State

- Keep components and Hooks pure and render idempotently for the same inputs. Side effects belong outside render, not in calculations that React may rerun. [S1]
- Treat props, state, Hook arguments, and Hook results as immutable snapshots. Do not mutate them directly or mutate values after passing them to JSX. [S1]
- Derive renderable values from existing props and state instead of storing duplicate state and synchronizing it with an Effect. Handle user actions in their event handlers rather than using Effects to react to those actions indirectly. [S2]

## Effects and Asynchronous Work

- Use Effects to synchronize with external systems, not as the default way to compute derived data or organize application data flow. [S2, S3]
- Declare the reactive dependencies an Effect uses; restructure the code instead of suppressing dependency checks to force an assumed execution order. [S3]
- Return cleanup that undoes subscriptions, connections, timers, or other synchronization. Setup and cleanup must tolerate React's extra development checks. [S3]
- When fetching in an Effect, abort the obsolete request or ignore its result during cleanup so stale responses cannot overwrite newer state. Preserve the project's established data-loading mechanism rather than introducing a second one. [S3]

## Validation

This is the harness workflow, not a claim that React mandates a particular test runner:

- Reuse existing component and Hook test utilities and preserve lint checks. Test observable behavior rather than internal render counts unless those counts are the actual requirement.
- Cover relevant loading, error, success, and stale-response behavior. Use local browser tests for changed user-visible flows when available, and inspect the final state before claiming success.
- Follow the shared testing and temporary-artifact rules. Do not claim accessibility, performance, or browser correctness from compilation alone.

## Official Sources

- [S1: Rules of React and purity](https://react.dev/reference/rules)
- [S2: You might not need an Effect](https://react.dev/learn/you-might-not-need-an-effect)
- [S3: Synchronizing with Effects](https://react.dev/learn/synchronizing-with-effects)