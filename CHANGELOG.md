# Changelog

## 0.1.0-preview.0 — first public preview

- Separated the reusable runtime, Editor, Native UI, analysis, and optional MCP adapter into one Git-installable UPM package with a local development project.
- Kept legacy `PrincessStudio.*` assembly, namespace, GUID, and save identifiers for compatibility; existing Assets installations require the documented copy-first migration.
- Included editable samples, UI Toolkit connection tools, EN/KO documentation, and three guided tutorials.
- Fixed exception retry, shared-event previews, stale drafts on Load, and sample fixture dependencies found during the preview audit.
- Verified a pinned Git install, 74/74 EditMode tests, representative Windows play and dialogue, copied-project migration checks, MCP transport, and a one-run analysis UI preserve/compare/export/reopen path.

This is a limited preview on Unity 6000.6.0f1 for Windows. See [preview limits](docs/known-limits.md) and [readiness evidence](review/READINESS.md). Package version, save schema, and analysis runtime fingerprint are separate identifiers.
