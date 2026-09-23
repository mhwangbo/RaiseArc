---
id: connect-ui
title: Connect your UI to RaiseArc
---

**Start state:** a game and generated `NativeGame` scene from [the first game](first-game.md). The screen's `RaiseArcGame` owns the official session host. Its `RaiseArcUIDocument` selects that Game object. Stop Play before editing the UI.

1. Select **Screen — your UI document** in the Hierarchy. In its RaiseArc UI Document component, select **Edit bindings**, then **Open UI Builder**. In UI Builder's **Library → Standard**, drag a **Label** into the HUD. Name it `my-money`, set temporary text, and style it. Save the UXML.
2. Back in the `UIBindings` Inspector, select **Refresh targets**, **Add connection**, target `my-money (Label)`, **Display → Money**, **Action → None**, and **Save bindings**. Press Play. The label should show the current host money, changing only after a real command.
3. Add a standard **Button**, name it `my-plan-button`, and save the UXML. Add a connection for that Button with **Action → Open Panel** and panel `plan-panel`. Play and click once. The plan opens without advancing game time.
4. Open the generated `ActivityCard.uxml` in UI Builder. Move the label and cost display or change its USS styling, then save. Do not edit the package template; edit the copy beside your game. Add a new activity in Studio. On Play, a new card should appear automatically. Click the activity card (its internal UXML name is `place-activity`) and confirm that the chosen stable activity ID enters the draft slot.
5. Confirm the plan and use **Begin next activity**. The host should apply the activity exactly once. Restart and load to check that the UI reconnects to the restored host.

**Finished result:** your UXML, USS, and binding assets are editable in your game's `Assets/RaiseArcGames` folder. Labels and buttons use standard UI Toolkit elements; no user C# is required for these listed bindings. A custom game rule outside the available command set needs a developer extension.

**Troubleshooting:** a missing target usually means the UXML Name differs from the binding target. A wrong type is reported by binding validation. If a card disappears after filtering or sorting, check its data-bound stable ID rather than its row position. Package-install validation of this full flow is still pending.
