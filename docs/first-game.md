---
id: first-game
title: Make your first raising game
---

**Start state:** a new Unity 6000.6.0f1 project with RaiseArc installed as described in [installation](installation.md). The package's native UI example creates an editable game and its matching screen together. The authoring and Windows player steps still require end-to-end validation.

1. Open **Window → RaiseArc → Samples → Create native UI example**. This creates a seven-day starter game and its matching `NativeGame.unity` under your project's `Assets/RaiseArcGames`. Open the generated game asset in Studio. Do not run this menu again after editing: it creates another example game.
2. In this game's **Activities** page, select **Study**. Change its displayed name and description, then save. Add one more activity with a distinct name, cost, and stat effect. The content list should show both; each needs a stable ID.
3. In **Events**, edit the starter invitation to contain two dialogue lines and two choices. Check each choice's effect and save. In **Endings**, edit the two starter outcomes: give one a stat threshold and leave one fallback. Validation should show no broken references.
4. Open the `NativeGame.unity` created in step 1 and press **Play**. Its plan screen should list the activities you edited in that same game. If a template is missing, record the error.
5. Choose activities, confirm the plan, and run one activity. Confirm that money and the chosen stat change once. Continue through the invitation's dialogue and make a choice; the next activity starts only after an explicit resume.
6. Press **Save** during a pending choice. Stop Play, start again, press **Load**, and confirm that the same choice and plan remain. Loading should not perform another activity. Continue to one of the endings.
7. Add the generated scene to **File → Build Profiles → Scene List**. Select a Windows desktop profile, build into a new empty output folder, and start the resulting executable. Check activity, dialogue, save, restart, load, and ending in the player. Building alone does not establish player behavior.

**Finished result:** your editable game asset and UI files stay in your project's `Assets`; the Windows build is in the folder you selected. If validation shows a missing reference, return to the relevant content page and fix it before building.

This guide must be walked through on the Git-installed package before it can be marked verified.
