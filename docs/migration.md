---
id: migration
title: Upgrade from PrincessStudio
---

**Save your changes, quit Unity, back up the project, and test every upgrade on a copy with a recorded Unity version.** One copied Assets-install project and two matching saves were checked with RaiseArc's `5e1cd6355a81dc679db41162bf5062cb66ad97af` Git package on Unity 6000.6.0f1. This does not establish compatibility for every project or save. Unsaved Editor memory does not carry across a restart; already saved user data must remain intact.

The new package keeps `PrincessStudio.*` assemblies, namespaces, serialized types, GUIDs, save IDs, Localization table identifiers, and the `princess-studio-` MCP pipe discriminator for compatibility. User-facing menus and new product material use RaiseArc. Those legacy strings are deliberate and are not a global search-and-replace target.

Do not install `Assets/PrincessStudio` and the UPM package together: they contain duplicate scripts and assembly definitions. In a project copy, inventory any files you edited in the old folder, including their `.meta` files. Move user-owned content to a separate project folder, then remove only the verified old package files and install the new package. Preserve `.meta` GUIDs on migrated assets. Reopen scenes and prefabs and inspect for Missing Script before touching the original.

Test a save copied from the old project with a pending dialogue choice, draft and confirmed plan, records, and random state. A failed load or runtime-fingerprint mismatch must leave the original save intact. RaiseArc must not silently apply a reward during Load. In the checked copy, two real saves loaded with the same serialized state and unchanged file bytes; one contained a confirmed plan, cursor and pending event. An unsaved draft, every older schema, and actual screen behavior remain unverified. Do not upgrade a sole production project.

### Legacy sample font in a copied project

The checked project's old generated `NativeGame.uss` files referenced `project://database/Assets/PrincessStudio/Samples/NativeUI/NotoSansKR-Regular.otf`. Localization asset tables and game assets also referred to the old sample font GUID `66ea8236994da9543bf2224dc889d085`. Removing the old Assets installation left that GUID without an Addressables location. In Play Mode, Unity reported a missing stylesheet asset and `InvalidKeyException` / `GetAssetAsync failed to load the asset` for `UnityEngine.Font`. Treat either warning as an incomplete migration, even if fallback text appears.

In a **project copy only**, inventory the affected font, stylesheet, asset tables, and Addressables group. Back up each file you will change. The checked copy was repaired by placing the package's reviewed OFL `Editor/Templates/NativeUI/NotoSansKR-Regular.otf` bytes in a separate user-owned `Assets/RaiseArcLegacyAssets/NotoSansKR.ttf` file with the old font's preserved `.meta` GUID, restoring that GUID to the project's Default Local Group, and changing only the two known `NativeGame.uss` font URLs to `project://database/Packages/io.github.mhwangbo.raisearc/Editor/Templates/NativeUI/NotoSansKR-Regular.otf`. Unity imported the replacement as a font, its bytes matched the packaged OFL font, and the checked scene then displayed Korean text without those two errors. Keep the package's OFL notice with any redistributed copy of the font.

This is a targeted example for the recorded GUID and two generated stylesheets, not a global replacement for user fonts or other missing assets. Keep the old files in the private backup; do not put excluded original art or an unverified font into the public package. Confirm the replacement's Addressables entry, the UI font, and save/restart/load in your own copied game before changing the original. The checked copy's basic UI save/restart/load displayed the same money and wisdom with no automatic reward; older complex saves were checked by API, not visually replayed.

The Unity game's title and company settings belong to the game creator and must not be renamed to RaiseArc.
