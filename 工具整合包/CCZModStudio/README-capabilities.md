# CCZModStudio Capabilities

## Main Creative Editors

- Role editor: character fields, biographies, battle quotes, R/S image IDs, backups, and reread verification.
- Job editor: job details, terrain/movement tables, strategy learning, job effects, and matrix tables.
- Item and shop editors: item data, descriptions, icon previews, item effect catalog, and shop slot editing.
- Battlefield editor: S scenario loading, map preview, deployment candidates, controlled deployment writes, and battlefield notes.
- Script editor: legacy R/S eex tree, command editing through verified legacy dialogs, text writes, command notes, and structure save path.
- R scene editor: R scenario preview, actor placement drafts, command navigation, and controlled coordinate write helpers.
- Map and terrain tools: map preview, Hexzmap probing, terrain overlay, map workbench, terrain painting, and map image replacement.

## Supporting Tools

- Resource indexing, diagnostics, replacement preview, backup restore, EEX/Ls probes, byte heatmaps, and cross-file comparison.
- Image resource catalog, E5 `110` entry preview/replacement, E5 batch replacement, and DLL icon preview/replacement.
- Creator notes, workflow guide, project audit, test-copy diff, backup history, release copy generation, and delivery reports.
- Local MCP server exposing project detection, tables, scenarios, resources, images, notes, knowledge entries, and guarded write tools.

## Write Boundary

- Default target is Cao Cao Zhuan enhanced 6.5 unencrypted project data.
- Writes must use validated target paths, backups, reread verification, structured reports, and version guards.
- 6.6/6.6x material is reference-only until separately verified.
- Current `Hexzmap.e5=44840` sample differs from the existing write guard baseline `45254`; direct Hexzmap writes remain blocked until that evidence is resolved.
