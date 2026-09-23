---
id: migration
title: Upgrade from PrincessStudio
---

**Test every upgrade on a copy with a recorded Unity version and backup.** One copied Assets-install project and two matching saves were checked with RaiseArc's `4488a95893c951ca628e1eae274016af317bf207` Git package on Unity 6000.6.0f1. This does not establish compatibility for every project or save.

The new package keeps `PrincessStudio.*` assemblies, namespaces, serialized types, GUIDs, save IDs, Localization table identifiers, and the `princess-studio-` MCP pipe discriminator for compatibility. User-facing menus and new product material use RaiseArc. Those legacy strings are deliberate and are not a global search-and-replace target.

Do not install `Assets/PrincessStudio` and the UPM package together: they contain duplicate scripts and assembly definitions. In a project copy, inventory any files you edited in the old folder, including their `.meta` files. Move user-owned content to a separate project folder, then remove only the verified old package files and install the new package. Preserve `.meta` GUIDs on migrated assets. Reopen scenes and prefabs and inspect for Missing Script before touching the original.

Test a save copied from the old project with a pending dialogue choice, draft and confirmed plan, records, and random state. A failed load or runtime-fingerprint mismatch must leave the original save intact. RaiseArc must not silently apply a reward during Load. In the checked copy, two real saves loaded with the same serialized state and unchanged file bytes; one contained a confirmed plan, cursor and pending event. An unsaved draft, every older schema, and actual screen behavior remain unverified. Do not upgrade a sole production project.

Unity's Addressables import removed an entry for a font inside the old `Assets/PrincessStudio` folder from the copied project's Default Local Group. Review Addressables changes against the backup before applying this process to another project.

The Unity game's title and company settings belong to the game creator and must not be renamed to RaiseArc.
