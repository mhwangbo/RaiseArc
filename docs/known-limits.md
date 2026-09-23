---
id: known-limits
title: Preview status and limits
---

The private `0.1.0-preview.0` candidate compiles in a fresh Unity 6000.6.0f1 Windows development project through a local UPM path. Native UI, monthly, next-day, and Composer scene creation ran headlessly. These checks do not establish Play Mode or Windows player behavior.

Open blockers:

- The source audit has not covered every first-party file and every historical Git object or asset.
- The independent Git install, representative play, Windows executable, and old-project/save migration have not passed.
- Several editor tools still use old `Assets/PrincessStudio` sample paths and need package-aware loading.
- Bundled font binary provenance requires an independent check.
- The optional MCP adapter's actual transport and end-to-end project selection need a fresh installed-package test.
- Documentation steps have not been walked through on the separated package or by a human beginner.

MCP is optional and Windows Editor only in the current implementation. UI Toolkit is the supported no-code binding surface; arbitrary new game commands need code. Analysis displays its own budget and unsupported boundaries. Old exact replay needs the matching runtime fingerprint.
