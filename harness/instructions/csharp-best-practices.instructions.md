---
description: "Use when planning, writing, reviewing, or testing C# and .NET code, dependency injection, async I/O, or ASP.NET Core services. Includes official sources and version checks."
applyTo: "**/*.cs,**/*.csproj,**/Directory.Build.props,**/Directory.Build.targets"
---

# C# and .NET Practices

## Scope and Evidence

- Check the target framework, SDK selection, language version, nullable settings, analyzers, and `.editorconfig` before choosing syntax or changing configuration. Do not silently upgrade them.
- Microsoft describes its coding conventions as conventions for documentation and samples, not a universal standard. Prefer an established project convention when it differs only in style. [S1]
- The sources below were consulted on 2026-11-20. Match version-specific documentation to the target project; these notes do not prove that a particular API exists in its installed version.

## Implementation and Review

- Keep code clear and focused. Use descriptive names, specific exception types, and catch exceptions only when the code can handle them appropriately. Follow the configured naming and formatting rules. [S1]
- Use asynchronous APIs for I/O when available and await through the call chain. In ASP.NET Core request paths, do not block with `Task.Wait` or `.Result`, and do not wrap synchronous I/O in `Task.Run` to pretend it is asynchronous. [S1, S2]
- Prefer explicit constructor dependencies over static service access or service location. Keep services small and testable; avoid building a second service provider during registration. [S3]
- Respect dependency-injection lifetimes. Do not capture a scoped service in a singleton or use request-scoped services after their request ends; create an appropriate scope for background work. Singleton services must be thread-safe. [S2, S3]
- Let the container dispose services it creates. Code that creates an object outside the container owns its disposal; do not manually dispose a container-owned dependency. [S3]
- For ASP.NET Core, do not access `HttpContext` concurrently or after a request has completed. Preserve request and background-work lifetime boundaries. [S2]

## Validation

This is the harness workflow, not a claim that Microsoft mandates a particular test framework:

- Use the repository's existing test framework and SDK-compatible build/test commands. Do not assume xUnit, NUnit, MSTest, or a particular coverage collector is installed.
- Apply the shared red-green-refactor and evidence rules to changed behavior. Cover error handling, asynchronous completion, and dependency lifetimes when those are affected.
- Report the exact checks run, their results, and anything blocked; a successful build alone does not establish runtime correctness.

## Official Sources

- [S1: Common C# code conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- [S2: ASP.NET Core best practices](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/best-practices)
- [S3: .NET dependency injection guidelines](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines)