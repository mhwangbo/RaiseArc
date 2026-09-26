# RaiseArc

RaiseArc is a Unity toolkit for building raising games: activities, conditions, effects, events, dialogue, endings, plans, saves, analysis, and UI Toolkit bindings.

**Status: 0.1.0-preview.0, preview ready with limits.** The package was installed and exercised from a fixed Git commit in a clean Unity 6000.6.0f1 Windows consumer project. Read the [limits](docs/known-limits.md) and [readiness evidence](review/READINESS.md) before upgrading an existing project.

Start with the [documentation site](https://mhwangbo.github.io/RaiseArc/) or [installation guide](docs/installation.md). The verified package URL is `https://github.com/mhwangbo/RaiseArc.git?path=/Packages/io.github.mhwangbo.raisearc#5e1cd6355a81dc679db41162bf5062cb66ad97af`.

## Layout

- `Packages/io.github.mhwangbo.raisearc`: the package source.
- `DevProject`: Unity 6000.6.0f1 development project that references the package by a relative local path.
- `website`: documentation site source.
- `review`: baseline, exclusions, findings, and remaining validation.

The package keeps legacy `PrincessStudio.*` assemblies and serialized identifiers while migration is tested. Do not install this package beside the old `Assets/PrincessStudio` folder in the same Unity project.

## Local development

Open `DevProject` with Unity 6000.6.0f1. It references `file:../../Packages/io.github.mhwangbo.raisearc` from its package manifest. The consumer validation used the fixed Git URL above, separately from this local development path.

Documentation: `cd website && npm ci && npm run build`. Site deployment is a manually dispatched GitHub Actions workflow.

Copyright 2026 Mi Hwangbo. First-party code is Apache-2.0; bundled font material retains its own OFL terms. See the package third-party notice.
