"""Bounded credential-pattern scan of every reachable blob in this new Git history.

This is a gate for obvious credentials, not a complete security or rights audit.
Only pattern names and blob paths are printed; matching bytes are never printed.
"""

import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PATTERNS = {
    "private-key-header": re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
    "github-token": re.compile(rb"(?:github_pat_[A-Za-z0-9_]{30,}|gh[oprsu]_[A-Za-z0-9]{30,})"),
    "aws-access-key": re.compile(rb"(?:AKIA|ASIA)[A-Z0-9]{16}"),
    "slack-token": re.compile(rb"xox[baprs]-[A-Za-z0-9-]{20,}"),
    "openai-style-key": re.compile(rb"sk-(?:proj-)?[A-Za-z0-9_-]{40,}"),
    "long-assigned-secret": re.compile(
        rb"(?i)(?:api[_-]?key|client[_-]?secret|access[_-]?token|password)\s*[:=]\s*['\"][A-Za-z0-9/_+=.-]{24,}['\"]"
    ),
}


def git(*args: str) -> bytes:
    return subprocess.check_output(["git", "-c", f"safe.directory={ROOT.as_posix()}", *args], cwd=ROOT)


def main() -> int:
    objects = git("rev-list", "--objects", "--all").decode().splitlines()
    examined = 0
    binary = []
    findings = []
    for entry in objects:
        oid, _, path = entry.partition(" ")
        if git("cat-file", "-t", oid) != b"blob\n":
            continue
        data = git("cat-file", "blob", oid)
        examined += 1
        if b"\0" in data:
            binary.append(path or oid)
        for name, pattern in PATTERNS.items():
            if pattern.search(data):
                findings.append((name, path or oid))
    print(f"Scanned {examined} reachable blobs; binary blobs: {len(binary)}; matches: {len(findings)}")
    for name, path in findings:
        print(f"MATCH {name}: {path}")
    for path in binary:
        print(f"BINARY {path}")
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main())
