---
id: installation
title: Installation
---

## Before you start

Install Unity 6000.6.0f1 with Windows Build Support through Unity Hub. Create a new Unity 2D project. The private Git candidate below resolved and compiled in a fresh consumer project; the full authoring and player walkthrough is still under validation.

1. In Unity, open **Window → Package Management → Package Manager**.
2. Select **+ → Install package from git URL**.
3. Enter `https://github.com/mhwangbo/RaiseArc.git?path=/Packages/io.github.mhwangbo.raisearc#22a1c1f0226949acbb0f052ab2013749061ca565`. This repository is private and requires access. Use a newer commit only after its installation has been checked.
4. Wait for package resolution. Confirm that **RaiseArc** appears and that the Console has no red compile errors.
5. Open **Window → RaiseArc** to see authoring tools. MCP is optional and is not needed for this check.

The package declares Unity Localization, Addressables, and uGUI dependencies. Unity Package Manager resolves them. The exact supported version range is still being validated; 6000.6.0f1 is the currently checked Editor.

### Local development

Open the repository's `DevProject` with Unity 6000.6.0f1. Its manifest references the sibling package as `file:../../Packages/io.github.mhwangbo.raisearc`. Edit source in `Packages/io.github.mhwangbo.raisearc`; the DevProject has no separate source copy.

### If installation fails

- **Package not found:** check the repository permission, commit, and `path` parameter.
- **Duplicate type or assembly:** the old `Assets/PrincessStudio` tree is still installed. Make a backup and follow [migration](migration.md); do not delete user edits by folder name.
- **Red Console errors:** record the first error and package version. Do not disable package checks to hide it.
