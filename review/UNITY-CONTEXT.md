# Unity project context

Analyzed 2026-09-23 from original `PrincessPlugin` HEAD `62e83189f3d0a166184b037c384132a136bbb8be` plus captured working changes.

- Unity Editor: 6000.6.0f1 (revision f7f8ed4d1e24), Windows. The separated `DevProject` was freshly created with that Editor.
- Product source: one UPM candidate at `Packages/io.github.mhwangbo.raisearc`. `DevProject/Packages/manifest.json` references it by a relative local `file:` path.
- Core runtime assembly remains `PrincessStudio.Core`; Unity host `PrincessStudio.Unity`; UI Toolkit runtime `RaiseArc.NativeUI`; Localization runtime `PrincessStudio.Localization`. Editor and graph assemblies are editor-only. Names are legacy compatibility identifiers.
- Package dependencies declared: Localization 1.5.13, Addressables 2.11.2, uGUI 2.6.0. Unity Test Framework 1.8.0 is in DevProject only. MCP Python adapter is optional and uses a Windows named pipe to an explicitly started Editor target.
- Runtime is designed to be independent of Editor graph code. Studio, Graph, and MCP should use the same revision-checked Authoring API.
- Original project includes sample scenes and game content under Assets. These were excluded from the candidate. The DevProject currently has no product sample scene or player build profile.
- Original project had EditMode and PlayMode tests. They were not copied into the candidate because several still require old sample paths and assemblies. A new candidate test suite and clean consumer run are pending.
- Available validation tooling: installed Unity 6000.6.0f1 Editor, direct batch-mode import, local Git, Node 22, npm 10, Docusaurus 3.10.2. No connected Unity MCP provider was verified.
- High-confidence check: fresh local UPM import compiled without C# errors. Lower-confidence areas: sample menus, saves, analysis, migration, Windows player, and external MCP transport.

Source evidence: original `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json`, `Packages/packages-lock.json`, 14 product asmdefs, selected runtime/editor snippets, and the fresh `devproject-import.log`. See `READINESS.md` for limits and findings.
