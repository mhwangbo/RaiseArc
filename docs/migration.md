---
id: migration
title: Upgrade from PrincessStudio
---

**This migration is not yet verified. Keep the old project and saves unchanged.** Test on a copy with a recorded Unity version and backup.

The new package keeps `PrincessStudio.*` assemblies, namespaces, serialized types, GUIDs, save IDs, Localization table identifiers, and the `princess-studio-` MCP pipe discriminator for compatibility. User-facing menus and new product material use RaiseArc. Those legacy strings are deliberate and are not a global search-and-replace target.

Do not install `Assets/PrincessStudio` and the UPM package together: they contain duplicate scripts and assembly definitions. In a project copy, inventory any files you edited in the old folder, including their `.meta` files. Move user-owned content to a separate project folder, then remove only the verified old package files and install the new package. Preserve `.meta` GUIDs on migrated assets. Reopen scenes and prefabs and inspect for Missing Script before touching the original.

Test a save copied from the old project with a pending dialogue choice, draft and confirmed plan, records, and random state. A failed load or runtime-fingerprint mismatch must leave the original save intact. RaiseArc must not silently apply a reward during Load. The current candidate has not passed this copy-project test, so do not upgrade a sole production project.

The Unity game's title and company settings belong to the game creator and must not be renamed to RaiseArc.
