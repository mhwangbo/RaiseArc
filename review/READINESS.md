# RaiseArc private candidate audit — 2026-09-23

## Verdict

**NOT READY for public open-source preview.** The separated package compiles in a new local Unity project and the EN/KO documentation site builds, but actual Git installation, representative play, migration, complete source and rights review, and Windows player checks remain open. Do not make the repository public, publish a Release, or deploy Pages from this candidate.

## Fixed baseline and provenance

- Source project: `D:/Projects/Unity/PrincessPlugin`, Git root at that path, branch `codex/native-ui`, HEAD `62e83189f3d0a166184b037c384132a136bbb8be`.
- At capture, 12 tracked files were modified and 117 untracked files existed. They include latest monthly and next-day UI work. The working changes were not reset or stashed. Original diff, status, untracked-file copies, and SHA-256 inventory are in the original project's ignored `.utmp/RaiseArcBaseline-20260923`.
- The source repository had 810 tracked files, 14 local refs, and 1,773 reachable object/path entries. Its local `origin/main` was 36 commits behind the working branch. GitHub's current `mhwangbo/PrincessPlugin` main was independently read as `0de4c5e... Initial check-in`.
- The destination `mhwangbo/RaiseArc` already exists as an **empty private** repository with the authenticated `mhwangbo` account as admin. No source has been pushed.
- Candidate package inventory with SHA-256 is `package-sha256.tsv`. Per-source review state is `source-review-status.tsv`. The original Git history is not being imported into this candidate.

## Scope and separation

The candidate's one source root is `Packages/io.github.mhwangbo.raisearc`. It contains copied `Runtime`, `Editor`, and optional Python MCP adapter source, plus the Native UI, monthly, next-day, and optional Composer templates. The first-party Composer sample controller code is kept in `Runtime/Composer` so the easy screen entry can compile without a separate game project. The templates keep their `.meta` files. `DevProject` references this one package by a relative local UPM path and does not hold a second product copy. Its successful C# import is recorded in `devproject-import.log`.

Excluded from the candidate: `Assets/PrincessStudioContent`, `Assets/RaiseArcGames`, game/QA/making projects, analysis results, logs, videos, old sample scenes, sample sprites, the second Noto TTF, original `ProjectSettings`, and old `Assets/PrincessStudio/Samples` game data and panel assets. They remain in the original workspace. The sample sprites' authorship and rights have not been independently established; they will not enter a public package by default. Native UI and Composer now generate plain project-owned placeholders instead. Other old sample paths remain a blocker.

The candidate includes 137 C#/Python source files. Ten named files received a focused source read; the remainder have only inventory and pattern scanning. See the TSV. This is **not** a claim of complete code review or independent second review.

## Confirmed findings

| ID | Priority | Location | Evidence and impact | Minimum resolution and check |
| --- | --- | --- | --- | --- |
| RA-01 | P1 | `Editor/Localization/SampleSceneBuilder.cs:21-39`; `Editor/Graph/GraphShowcaseBuilder.cs:13-20`; `Editor/Integration/RaiseArcSimulationExample.cs:13`; Editor graph tests | Several optional sample builders and tests still read `Assets/PrincessStudio/Samples`, which is absent from the package candidate. Their broken menu entries were removed; Graph Workbench no longer falls back to a missing demo asset, and Studio font uses the included package OTF. Native UI, monthly, next-day, and Composer creation passed headless generation. The retained builder APIs/tests still need portable fixtures and menu validation. | Replace old sample reads with reviewed editable samples or generated content, then run all advertised menus and tests from a clean Git-installed project. |
| RA-02 | P1 | Package structure and `DevProject` manifest | Unity import compiled through `file:../../Packages/...`; three Native UI scenes and a Composer scene generated in that local DevProject. No commit-specific Git install, Play Mode walkthrough, or Windows player check has been done. | Commit reviewed candidate, install by Git URL in a separate consumer project, and run dependencies, authoring/play, UI, and MCP-off checks. |
| RA-03 | P1 | `docs/migration.md`; legacy asmdefs and GUIDs | Old assemblies, namespaces, serialized IDs, saves, Localization IDs, and MCP pipe names remain. This preserves identifiers, but coexistence with `Assets/PrincessStudio` creates duplicate assemblies. No copied old project/save migration was run. | Test a backed-up old-project copy, verify Missing Script and save-state fidelity, and document a reversible file-by-file removal. |
| RA-04 | P1, resolved for included font | `Editor/Templates/NativeUI/NotoSansKR-Regular.otf`; `Third Party Notices.md` | The included OTF and license match official `notofonts/noto-cjk` tag `Sans2.004` Git blob IDs `b8c11425a25c74dac1b51b28341ff1ef379f2ffa` and `d952d62c065f3f35fb83a173496e90b21525aef3`. The OFL 1.1 notice is retained. Excluded art remains unreviewed. | Keep the matched binary and notice together; keep unverified art excluded. |
| RA-05 | P1 | `source-review-status.tsv` | Only 10 of 137 candidate source files had focused reads. Rule, analysis, serialization, UI lifecycle, and external-input paths have not all been traced. Existing test counts cannot close this gap. | Review each first-party file and trace high-risk call chains; add small regression checks for confirmed defects. |
| RA-06 | P2 | `Runtime/Unity/Persistence/SaveStore.cs:49-131` | Save writes a temporary file and replaces the primary with backup; Load checks project/content and participant set and attempts rollback if a participant restore fails. Rollback failures are aggregated. This is evidence of an explicit failure path, not evidence that all save migrations are safe. | Exercise corrupt, unsupported, participant-failure, and old-save copies through the separated package. |
| RA-07 | P2 | `Editor/Integration/WindowsPipeFactory.cs:19-41`, `AuthoringPipeServer.Start`, `Runtime/Unity/AuthoringCommandGateway.ExecuteJson` | MCP is explicitly started for a selected project, names a pipe from a project-path hash, uses a Windows owner-SID ACL and remote-client rejection, caps command text at 256 KiB, and allowlists operations with revision requirements for mutations. This is source evidence only; actual adapter transport, target switching, input depth/work budget, and installed-client behavior remain untested. | Test real named-pipe requests, stale revision rejection, project switch, path handling, and bounded workload. |

The fixed-source code reviewed here does not justify relaxing analysis fingerprints, treating unsupported extensions as success, or declaring a complete security audit. No confirmed P0 secret or data-loss defect was found in the limited scan; that is not proof of absence.

## Name migration map

| Surface | Candidate treatment | Reason |
| --- | --- | --- |
| Product name, new package ID, docs, menus | RaiseArc / `io.github.mhwangbo.raisearc` | New user path |
| `PrincessStudio.*` assemblies and namespaces | Retained | Existing serialized script and type references |
| `Assets/PrincessStudio` and `.meta` GUIDs | Original preserved; selected templates copied with GUIDs | No untested asset rewrite |
| Save IDs, content IDs, Localization table IDs | Retained | Existing data compatibility |
| `princess-studio-` pipe discriminator | Retained | Existing adapter protocol |
| Historical validation files | Left in original project | Preserve evidence with original names |

Removal condition for each legacy identifier: a copied-project, scene/prefab, save, and protocol migration test must pass with a reversible path. `MovedFrom` alone would not cover JSON, files, and pipe names.

## Supply chain and rights

| Material | Source/version | License/notice | Included? | Evidence / uncertainty |
| --- | --- | --- | --- | --- |
| First-party code and docs | Original local Git branch; contributors in history: `mhwangbo` and `Mi Hwangbo` | Intended Apache-2.0, copyright Mi Hwangbo | Yes | User confirmed name; complete contribution/embedded-code audit pending |
| Unity Localization | UPM 1.5.13 | Unity package terms | Declared dependency, not copied | Source manifest and lock |
| Unity Addressables | UPM 2.11.2 | Unity package terms | Declared dependency, not copied | Source lock, package asmdef references |
| Unity uGUI | UPM 2.6.0 | Unity package terms | Declared dependency, not copied | Source manifest |
| Noto Sans KR OTF | notofonts/noto-cjk Sans2.004 | SIL OFL 1.1; full notice included | Yes | Official tag blob IDs match included OTF and license |
| Original sample PNGs, scenes, game content, second font | Original Assets tree | Incomplete independent provenance | No | Rights unresolved; excluded |
| Python MCP adapter | Original local first-party source; Python standard library | First-party Apache-2.0 intent; Python not bundled | Yes, optional | Transport and distribution review pending |

A filename/pattern scan of candidate and original history found no obvious key markers. It did not decrypt, parse every binary, inspect LFS payloads, or establish all rights. The fresh candidate history must be checked before any push.

## Validation ledger

| Check | Result |
| --- | --- |
| Original baseline capture | PASS: HEAD, status, working diff, untracked copies, hashes |
| Private destination identity and visibility | PASS: authenticated `mhwangbo`, empty private `mhwangbo/RaiseArc` |
| Independent local UPM compile | PASS: Unity 6000.6.0f1 import; no C# compile error in final Composer smoke log |
| Native UI, monthly, next-day, Composer generation | PASS: headless scene/assets creation in local DevProject; no Play Mode claim |
| Docusaurus EN/KO production build and link gate | PASS: `npm run build` |
| Browser language switch and Korean local search | PASS: Korean page and `계획` results visible |
| Narrow-screen visual layout | NOT RUN |
| Git URL consumer install, sample generation, authoring/play | NOT RUN |
| Old project and save migration | NOT RUN |
| MCP transport and References | NOT RUN |
| Analysis preservation/replay/export/reopen | NOT RUN |
| Windows build and executable behavior | NOT RUN |
| Human beginner usability | NOT RUN |

## Publication gate

The private repository may receive a reviewed candidate commit when file, rights, history, and dependency checks are complete. Its empty state is intentional now. Public visibility, Pages deployment, and Release publication require the user's separate final approval after blockers close.

## Continuation — 2026-09-23, after `e264644f`

This section records later work without replacing the earlier verdict or claiming complete review. The current public verdict remains **NOT READY**.

- Read-only peer reviews inspected a pinned `e264644f` snapshot. They traced core activity, planning, persistence, analysis, UI lifecycle, Authoring/Graph, and MCP methods. The current committed package has 134 C#/Python files: 58 have a recorded focused read (including 4 from the prior pass), and 76 still have only inventory/pattern scanning. Focused method reads are not full-file reviews. The source TSV now records each file's committed-byte SHA-256 and status. The codebase-memory service was intermittently unavailable; bounded direct reads were used when it failed.
- **RA-08, P1, confirmed by source path:** Studio's project picker changed the visible `GameProjectAsset` while the global MCP pipe retained the previous target. `StudioWindow.SetProject` now stops the old connection, and the UI shows the connected asset path. A focused regression was added. Actual named-pipe target switching is still untested.
- **RA-09, P2, confirmed by source path:** An exception in `Host.Resume()` could leave `RaiseArcGame.planRun` set and prevent later continuous runs. `ContinuePlan` now clears the handle in `finally`; an exception/retry regression is still needed.
- **RA-10, P2, confirmed by source path:** `PlaythroughSimulator` checked unexpanded event steps although `GameSession` expands shared flows. It now asks the session about the pending step. A synthetic shared-flow regression passed by direct Unity `-executeMethod` invocation; the standard Test Runner result is still pending.
- **RA-01 progress:** Studio's editable example is generated from first-party definitions into the user's `Assets` folder. Graph and Simulation Workbench styles load from the package. The game-screen prefab exporter uses the current project's generated prefab and rejects an existing target path. Obsolete distribution/sample builders that could only access excluded original files were removed. Graph tests and `GraphShowcaseBuilder` still require independent fixtures, so RA-01 remains open.
- The original Apache-2.0 `NOTICE` attribution was restored in the repository and package with the current font path. The original `0.3.0-preview.2-*` number belonged to an internal unitypackage candidate; the independent UPM package is `0.1.0-preview.0`. Neither number describes the save schema or analysis runtime fingerprint, and changing a version string does not establish compatibility.
- Unity 6000.6.0f1 imported the changed local package without C# compiler errors. A first filtered EditMode attempt discovered zero tests because the local package was not listed in `DevProject/Packages/manifest.json` `testables`; that entry was added. Later filtered attempts did not produce a completed result XML and must not be counted as passing. Direct invocation of the MCP switch test also ended without a completion marker; actual MCP startup remains unverified. The older Graph tests still read unavailable sample JSON.
- Pre-push peer audit of the initial five-commit history found no matches for common token/private-key patterns. A subsequent scan of the expanded local history examined 480 reachable blobs, found no listed credential-pattern matches, and found one binary: the verified OFL font. This is a bounded scan, not proof that all source rights or secrets have been audited. The original worktree-based `package-sha256.tsv` had line-ending mismatches; `Tools/commit_inventory.py` now records bytes from the committed Git tree and CI checks them.
- The reviewed candidate was pushed to the verified private `mhwangbo/RaiseArc` main branch at `b23e180059f0225bbcd395988c8ee89a92b02c6c`. No original refs were copied, and no visibility, Pages, or Release setting was changed. A freshly created `ConsumerProject` installed exactly that Git revision through UPM's `?path=/Packages/io.github.mhwangbo.raisearc#<full SHA>` syntax. Its lockfile records source `git`, hash `b23e180059f0225bbcd395988c8ee89a92b02c6c`, and all three declared dependencies; Unity 6000.6.0f1 imported and compiled it without C# errors. The package cache held 365 text files matching Git blob hashes after CRLF normalization, one exact font binary, and a `package.json` to which Unity added `_fingerprint`.
- Native UI sample generation in the Git-installed consumer wrote its scene, UXML, USS, bindings, placeholder images, and project data under consumer `Assets/RaiseArcGames`. Its importer warned that three copied USS templates still referred to the excluded old font path. Commit `d3cf4fb` changed those URLs to the packaged OTF; a fresh Git install of that revision and regenerated sample check are still needed before the warning can be closed.
