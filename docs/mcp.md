---
id: mcp
title: Optional MCP connection
---

Normal game creation and play do not need MCP, Python, or an LLM. The optional adapter currently supports Windows Editor and Python 3.10+ with only the standard library.

1. In Unity Studio, select the intended game project and use **LLM commands → Enable local MCP**. Copy the pipe name displayed there. It retains the `princess-studio-` prefix for existing client compatibility.
2. Locate `Integrations/MCP/raisearc_mcp.py` in the installed RaiseArc package. Configure your MCP client's stdio command as `python <absolute-path-to-raisearc_mcp.py> --pipe <name-shown-by-Unity>`. The path is local to that installation; do not paste a private machine path into a shared configuration.
3. Ask the client for `tools/list`. Start with `ReadProject` or `GetContentIndex` to confirm the project and current revision. For an edit such as `CreateActivity`, pass the returned `expectedRevision`. A stale revision must be rejected. Graph proposals use `PreviewChangeSet` and `CommitChangeSet` with a preview token.
4. Disable the MCP host in Studio when done. Switching Unity projects requires checking the displayed target and reconnecting to the new pipe.

The adapter allowlists tool names, bounds requests to 256 KiB, and rejects oversized responses. The Windows named pipe is configured for the current user and remote clients are rejected. These are source-level findings; a real installed-client transport and project-switch test remains a release gate.

The adapter's `tools/list` response is the schema for the installed version. Tool names include `ReadProject`, `CreateActivity`, `GetReferences`, `PreviewChangeSet`, and `CommitChangeSet`; Graph and analysis tools have additional fields and limits. Do not reuse an old copied schema when the installed version changes.
