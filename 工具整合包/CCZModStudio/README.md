# CCZModStudio 6.5

CCZModStudio is a WinForms integrated editor for the Cao Cao Zhuan enhanced 6.5 unencrypted MOD project. It focuses on creator-facing editing workflows instead of acting as a launcher for legacy tools.

## Current Focus

- Keep the 6.5 unencrypted baseline as the default write target.
- Preserve the existing safety loop: validate target path, back up, write, reread, and emit structured reports.
- Keep 6.6/6.6x material as reference-only until a separate version guard and local sample evidence exist.
- Reduce maintenance risk by splitting UI code mechanically before extracting runtime services.

## Entry Points

- Main app: `CCZModStudio\CCZModStudio.csproj`
- Smoke tests: `CCZModStudio.SmokeTests\CCZModStudio.SmokeTests.csproj`
- MCP server: `CCZModStudio.McpServer\CCZModStudio.McpServer.csproj`
- Shared runtime boundary: `CCZModStudio.Runtime\ARCHITECTURE.md`

## Docs

- Current capabilities: `README-capabilities.md`
- Verification commands: `README-verification.md`
- Historical notes: `README-history.md`
- Local knowledge base: `..\本地知识库\README.md`

## Verify

From `工具整合包`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\verify.ps1
```

Use `-RunWriteSmoke` only when a test-copy write smoke is intended. Current Hexzmap evidence is unresolved: the local sample is `44840` bytes while the write guard baseline is `45254`, so Hexzmap direct writes remain blocked until that mismatch is verified.
