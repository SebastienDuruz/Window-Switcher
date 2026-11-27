# Contributing to Window Switcher

Thank you for your interest in Window Switcher. This document describes how to report problems and how to propose changes. Read it before opening an issue or a pull request.

Each documentation file has a single role:

| File | Audience and purpose |
| --- | --- |
| [`README.md`](README.md) | Users: features, installation, configuration, and build from source. |
| `CONTRIBUTING.md` | Contributors: issues, branches, commit messages, and pull requests. |
| [`AGENTS.md`](AGENTS.md) | Normative engineering rules, applied during development and code review. |

## Before you start

- Search the [existing issues](https://github.com/SebastienDuruz/Window-Switcher/issues) before opening a new one.
- Open an issue before investing in a significant behavioral or architectural change, so the approach can be discussed first.
- Bug reports should include the application version and the non-sensitive diagnostic information shown under `Settings > About` (operating system, session type, desktop environment, preview mode, dependency status).
- Do not post `config.json` or log files publicly without reviewing them first. They can contain window identifiers, file paths, and other details about your system.

## Development setup

Follow the [Build from source](README.md#build-from-source) section of the README and, on Linux, install the [Linux build dependencies](README.md#linux-build-dependencies).

Run `make restore` (or `dotnet restore Window-Switcher.slnx` followed by `dotnet tool restore`) once after cloning. It also restores the local [CSharpier](https://csharpier.com/) formatter, whose version is pinned in `dotnet-tools.json`.

## Branching model

| Branch | Role |
| --- | --- |
| `dev` | Integration branch. **All pull requests target `dev`**, including external contributions. |
| `main` | Released versions. |
| `feature/<name>` | Work branches. Choose any descriptive name, for example `feature/kwin-rename`. |

The application version is defined once as `WindowSwitcherVersion` in `Directory.Build.props`. Only the maintainer changes it; do not bump it in a pull request.

## Commit messages

Use the format `<type>: <description>`, without a scope:

```text
fix: Avoid exception on remove prefix
```

and not `fix(pipewire): ...`.

| Type | Use for |
| --- | --- |
| `feat` | A new user-visible feature or behavior. |
| `fix` | A bug fix. |
| `refactor` | A code change that neither fixes a bug nor adds a feature. |
| `docs` | Documentation only. |
| `chore` | Maintenance, dependency or version updates. |
| `build` | Build scripts, packaging, or project files. |

Write the description in English, in the imperative mood, as one short summary line. Add a body when the reason for the change is not obvious from the summary.

## Pull requests

- Keep each pull request small, focused, and reviewable, with a single intent.
- Target the `dev` branch.
- Explain what changes and why, and link the related issue when there is one.
- State which platforms you tested manually: Windows, Linux X11, Linux Wayland on KDE Plasma, or Linux Wayland on another desktop. Say explicitly what could not be tested.

Include this checklist in the description and tick each item:

```markdown
- [ ] `dotnet build Window-Switcher.slnx` succeeds
- [ ] `dotnet test src/WindowSwitcher.Tests/WindowSwitcher.Tests.csproj` passes
- [ ] `dotnet csharpier check .` passes
- [ ] Tests are added or updated for business logic
- [ ] `README.md` is updated if user-visible behavior changes
- [ ] Any deviation from `AGENTS.md` is justified below
```

## Engineering rules

[`AGENTS.md`](AGENTS.md) is the normative reference and is applied during code review. In short:

- The UI layer contains no business logic, and ViewModels perform no I/O or operating-system calls.
- Every operating-system interaction goes through `WindowSwitcher.Lib`; OS-specific code lives in `src/WindowSwitcher.Lib/Data/Platform/WindowAccess`.
- Nullable reference types stay enabled, and the null-forgiving operator (`!`) is not used without a justification.
- I/O and long-running work are asynchronous; `.Result` and `.Wait()` are never used.
- Public APIs in `WindowSwitcher.Lib` have XML documentation.

## Logging, telemetry, and privacy

- Log through Serilog with constant message templates and named properties, using the levels described in `AGENTS.md`.
- Never write window titles, keystrokes, credentials, or DSNs to logs or to Sentry.
- Handled errors are logged only; they never create a Sentry event. Only unhandled exceptions are reported.

See [Privacy and error reporting](README.md#privacy-and-error-reporting) for what the application sends.

## Platform-specific changes

- Test changes on the platform they affect, and list in the pull request what you could not test.
- On Linux, the build requires native dependencies: a C17 compiler, `pkg-config`, and the PipeWire development headers. See [Linux build dependencies](README.md#linux-build-dependencies).

## Documentation and plans

- Screenshots go in `docs/<version>/`, for example `docs/0.8.0/`.

## License

Window Switcher is licensed under the [GNU General Public License v3.0](LICENSE). By submitting a contribution, you agree that it is published under the same license.
