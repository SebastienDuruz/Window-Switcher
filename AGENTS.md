# AGENTS

Purpose: Define strict, enforceable engineering guidelines for contributing to Window Switcher. This document targets senior .NET developers and MUST be followed during development and code review.

---

## Stack and Structure

- Solution: `Window-Switcher.sln`
- UI: `src/WindowSwitcher/` (Avalonia)
- Core/Library: `src/WindowSwitcher.Lib/`
- Tests: `src/WindowSwitcher.Tests/`
- Target framework: `.NET 10` (`net10.0`)
- MVVM: `CommunityToolkit.Mvvm`

---

## Build, Run, Test (Local)

- Build: `dotnet build Window-Switcher.sln`
- Run: `dotnet run --project src/WindowSwitcher/WindowSwitcher.csproj`
- Tests: `dotnet test src/WindowSwitcher.Tests/WindowSwitcher.Tests.csproj`

All changes MUST compile and all tests MUST pass before submission.

---

## Architecture Rules

- UI layer MUST NOT contain business logic.
- ViewModels MUST NOT perform direct I/O or OS calls.
- All system/OS interactions MUST go through `WindowSwitcher.Lib`.
- OS-specific code MUST be isolated in `src/WindowSwitcher.Lib/Data/WindowAccess`.
- Direct access from UI to OS APIs (including P/Invoke) is strictly forbidden.
- No cross-layer shortcuts are allowed.

---

## MVVM Rules

- ViewModels MUST be UI-agnostic.
- Code-behind MUST remain minimal and contain no business logic.
- Commands MUST be used instead of event handlers.
- Use `ObservableObject` and `[ObservableProperty]` consistently.

---

## Coding Conventions (Microsoft-Aligned)

### Nullability

- `<Nullable>enable</Nullable>` MUST remain enabled.
- Null-forgiving operator (`!`) MUST NOT be used unless strictly justified.
- Use `ArgumentNullException.ThrowIfNull` for validation.

### Asynchronous Programming

- All I/O and long-running operations MUST be asynchronous.
- Blocking calls (`.Result`, `.Wait()`) are strictly forbidden.
- UI thread MUST NEVER be blocked.
- UI updates MUST use `Dispatcher.UIThread`.

### Concurrency

- Long-running operations MUST support `CancellationToken`.
- Shared state MUST be protected (no unsafe concurrent access).
- Prefer immutable data over locking.
- Resources MUST be properly disposed (`IDisposable`).

### Collections

- Prefer `IReadOnlyCollection` (or stricter abstractions) for inputs.
- Avoid concurrent mutations of shared collections.

---

## API Design

- Public APIs MUST be minimal, explicit, and intention-revealing.
- Implementation details MUST NOT leak through public contracts.
- Prefer immutable models.
- Avoid `object`, `dynamic`, or weakly-typed APIs.

---

## Exceptions and Error Handling

- Exceptions MUST represent programmer errors or unexpected failures.
- Exceptions MUST NOT be used for control flow.
- Fail fast on invalid states.
- Expected errors MUST be handled and translated into user-facing messages.

---

## Configuration

- All configuration access MUST go through `ConfigFileAccessor`.
- Direct file I/O from the UI layer is forbidden.

---

## Logging

- All unexpected errors MUST be logged.
- Silent failures are forbidden.
- Logs MUST NOT contain sensitive data.

### Sentry (Error Tracking & Usage Metrics)
- Sentry is used to capture unhandled exceptions and basic usage metrics.
- Only the free tier of Sentry is used; event volume MUST be controlled.
- All unhandled exceptions MUST be reported to Sentry.
- Expected/user errors MUST NOT be sent as exceptions; use logging instead.
- PII and sensitive data MUST NOT be sent to Sentry (scrub or omit).
- Breadcrumbs SHOULD be used to provide minimal context for failures.
- Sampling SHOULD be applied when necessary to stay within free-tier limits.
- Network failures to Sentry MUST NOT impact application behavior.

---

## Performance

- UI interactions MUST remain responsive (no perceptible blocking).
- Avoid unnecessary allocations in hot paths.
- Expensive OS calls SHOULD be cached when appropriate.

---

## Testing Guidelines

- All business logic MUST be covered by unit tests.
- ViewModels MUST be testable without UI runtime.
- External dependencies MUST be mockable.
- Avoid static dependencies; prefer dependency injection.

---

## Naming Conventions

- Use PascalCase for public members.
- Use meaningful, intention-revealing names.
- Avoid abbreviations unless widely accepted.

---

## Quality Requirements

- Public APIs in `WindowSwitcher.Lib` MUST include XML documentation.
- Code MUST be readable, maintainable, and self-explanatory.

---

## Contribution Rules

- Changes MUST be small, focused, and reviewable.
- All changes SHOULD include tests when applicable.
- Code MUST comply with this document before submission.
- Any deviation MUST be explicitly justified in the PR.

---

## Non-Goals

- This document does not cover UI/UX design decisions.
- This document does not replace architectural discussions for major changes.

