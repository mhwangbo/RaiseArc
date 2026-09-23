---
id: analyze-ending
title: Check an ending path and an economy change
---

**Start state:** a saved game asset with at least two activities and a stat-gated ending. Save Studio or Workbench edits before analysis; Explorer uses a detached saved snapshot.

1. In Studio, select the ending and choose **Find a path to the saved ending**. In Explorer, select that ending as the goal. Add an activity restriction if you want to exclude an action; an empty allow list leaves valid activities available.
2. Select **Find path / rerun test**. Read the **goal verdict** and **stop reason** separately. Open the discovered path and inspect its actual money and stat changes. Select **Verify replay** to execute the listed inputs again; a displayed path alone is not replay evidence.
3. Select **Save test definition**, then **Preserve result and original content**. Record the result ID. The preserved result belongs to its original content snapshot and runtime fingerprint.
4. Return to Studio, increase one activity's cost, and save the game asset. In the saved ending test, use **Recheck inputs on current candidate** to test the old path against the changed content. Compare its goal verdict and committed money change with the preserved original; a failed old path is a meaningful outcome. Ask Explorer for another path within its displayed budget and restrictions.
5. The ending-test panel preserves and rechecks paths; it does not export a comparison file. To compare repeated play samples, open **Balance test**, save a balance test for the same game, run and preserve one result per candidate, then use **Compare preserved candidates → Export comparison…**. Use **Open comparison file…** to reopen that JSON and check its result IDs and candidate identities. These balance samples are separate evidence from the fixed ending path. A missing old runtime fingerprint must be reported as unsupported rather than marked replayed.

**Finished result:** the game asset remains under your project's `Assets`. Local analysis results are stored under `RaiseArcAnalysisResults` at the Unity project root and need their own backup if you want to retain them. They are not player saves.

**Troubleshooting:** if no path is found, inspect stop reason, restrictions, and search budget before changing the goal. A completed run is not necessarily a goal pass. Fixed confirmed plans are replayed as fixed inputs; this is not exhaustive verification of every future daily or monthly planning strategy. The separated-package UI flow and balance export/reopen step still require validation.
