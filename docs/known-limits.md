---
id: known-limits
title: Preview status and limits
---

The `0.1.0-preview.0` package was installed from a fixed Git commit in a fresh Unity 6000.6.0f1 Windows project. Full-source review records cover the 136 committed C#/Python package sources; the completed EditMode suite passed 74/74. A representative Windows player, two-line dialogue and return to play, installed MCP transport, one-run analysis comparison UI, and a copied old-project migration path were checked. These are bounded checks, not a security guarantee or exhaustive compatibility test.

Preview limits:

- Unity 6000.6.0f1 on Windows is the checked Editor and player combination. Other Unity versions and platforms were not validated.
- Upgrade an old Assets installation only after saving changes, closing Unity, backing up the project, and rehearsing in a copy. Do not install the old and new assemblies together. Unsaved Editor memory is outside the upgrade guarantee; saved files and ordinary drafts must remain intact.
- The copied legacy project needed a targeted font/Addressables migration. Other project-specific art, fonts, and user UI references may need individual migration. The original game and saves were preserved.
- Representative play and one deterministic analysis sample do not cover every event, save, plan, or random outcome. The analysis window labels budgets and unsupported boundaries; preserved comparisons do not imply exact replay after content changes.
- The tutorials were checked by an agent against the installed candidate, with human help on some Unity controls. A longitudinal beginner usability study has not been performed.

MCP is optional and Windows Editor only in the current implementation. UI Toolkit is the supported no-code binding surface; arbitrary new game commands need code. Analysis displays its own budget and unsupported boundaries. Old exact replay needs the matching runtime fingerprint.
