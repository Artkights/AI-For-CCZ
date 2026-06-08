# CCZModStudio Verification

Use the repository-level verification script from `工具整合包`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\verify.ps1
```

The default run performs:

- `dotnet build .\CCZModStudio.sln`
- `--rs-smoke`
- `--legacy-mfc-dialog-smoke`
- MCP stdio validation
- active reference scan for old flat knowledge-base Markdown paths

Optional write smoke:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\verify.ps1 -RunWriteSmoke
```

The write smoke must only write test copies. Do not bypass the Hexzmap guard when the current sample size is still unresolved.
