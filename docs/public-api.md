---
id: public-api
title: Runtime and authoring API
---

Most creators use Studio and UI Toolkit bindings. Developers can create one `RaiseArc.Unity.RaiseArcSessionHost` for a game and connect multiple views to it. Keep it alive outside panels that can be hidden or recreated. Subscribe while a view is active, unsubscribe when removed, and refresh immediately after attaching. After Load or Restart, read fresh choices and stage data from the host.

```csharp
var host = new RaiseArc.Unity.RaiseArcSessionHost(projectAsset, saveDirectory, seed: 123);
host.Changed += RefreshScreen;
host.StartActivity(activityId);
```

Use host methods for mutations while it owns a plan. Direct session mutations can bypass plan coordination. A choice or module wait must be completed or cancelled through its official method, then explicitly resumed where required.

Editor integrations use `AuthoringService` with the current project revision. Studio, Graph Workbench, References, and MCP should share its validation and commit path. A custom editor host is responsible for Unity Undo, dirty marking, and persistence. An extension registers explicit `ICondition`, `IEffect`, `IGameModeModule`, or `ISaveParticipant` implementations; installed extension code is trusted application code, not a security sandbox.

`SaveStore` writes a versioned JSON payload with a checksum, temporary file, and backup. The checksum detects accidental damage, not tampering. A project or content mismatch requires an explicit migration. Register all save participants before loading.

These are preview contracts. Exact source and overloads in the installed candidate are authoritative until API stability is declared.
