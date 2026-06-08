# CCZModStudio.Runtime Boundary

`CCZModStudio.Runtime` is the headless service boundary shared by the WinForms app, MCP server, smoke tests, and initializer.

## Current State

- The runtime project links `Core`, `Formats`, and `Models` from `CCZModStudio`.
- This keeps MCP and CLI-style tools usable without duplicating code.
- The WinForms app still owns `MainForm`, dialogs, controls, visual layout, and direct user interaction.

## Rules

- Runtime code must not depend on `MainForm`, WinForms controls, message boxes, or UI tab state.
- Write services must keep target path validation, backup, reread verification, structured reports, and version guards.
- MCP tools should call runtime services and return structured payloads; they must not write diagnostics to stdout.
- New parsing, indexing, validation, and write logic should be added under `Core`, `Formats`, or `Models` first, then consumed by UI and MCP.

## Migration Path

1. Keep mechanical UI partial splits behavior-free.
2. Move non-UI helpers out of `MainForm` only when a build and smoke test can prove no behavior changed.
3. Prefer moving one feature family at a time, for example image catalog, battlefield deployment, or scenario command notes.
4. After each extraction, run `工具整合包\verify.ps1`.
