# RaiseArc

RaiseArc is a Unity toolkit for building raising games: activities, conditions, effects, events, dialogue, endings, plans, saves, analysis, and UI Toolkit bindings.

**Status: private productization candidate.** This checkout is not yet an installable public preview. See [the readiness audit](review/READINESS.md) before using it in an existing project.

## Layout

- `Packages/io.github.mhwangbo.raisearc`: one candidate package source.
- `DevProject`: Unity 6000.6.0f1 development project that references the package by a relative local path.
- `website`: documentation site source.
- `review`: baseline, exclusions, findings, and remaining validation.

The package keeps legacy `PrincessStudio.*` assemblies and serialized identifiers while migration is tested. Do not install this package beside the old `Assets/PrincessStudio` folder in the same Unity project.

## Local development

Open `DevProject` with Unity 6000.6.0f1. It references `file:../../Packages/io.github.mhwangbo.raisearc` from its package manifest. This local development path is not a published Git installation check.

Documentation: `cd website && npm ci && npm run build`. No site deployment runs on push.

Copyright 2026 Mi Hwangbo. First-party code is intended for Apache-2.0 distribution after the release audit. Bundled font material retains its own OFL terms; see the package third-party notice.
