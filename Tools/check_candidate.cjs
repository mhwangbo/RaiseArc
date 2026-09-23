// Read-only pre-push checks for the separated RaiseArc candidate.
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const pkg = path.join(root, 'Packages', 'io.github.mhwangbo.raisearc');
const errors = [];
const manifest = JSON.parse(fs.readFileSync(path.join(pkg, 'package.json'), 'utf8'));
if (manifest.name !== 'io.github.mhwangbo.raisearc') errors.push('package ID mismatch');
for (const dep of ['com.unity.localization', 'com.unity.addressables', 'com.unity.ugui'])
  if (!manifest.dependencies?.[dep]) errors.push(`missing dependency: ${dep}`);
for (const dir of ['Runtime', 'Editor', 'Integrations'])
  if (!fs.statSync(path.join(pkg, dir), { throwIfNoEntry: false })?.isDirectory())
    errors.push(`missing product directory: ${dir}`);

const files = [];
function walk(dir) {
  for (const item of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, item.name);
    if (item.isDirectory()) walk(full);
    else if (item.isFile()) files.push(full);
  }
}
walk(pkg);
const guids = new Map();
for (const file of files) {
  const rel = path.relative(pkg, file).replaceAll('\\', '/');
  if (/^(?:Library|Logs|RaiseArcGames|PrincessStudioContent)(?:\/|$)/.test(rel))
    errors.push(`game or generated content: ${rel}`);
  if (fs.statSync(file).size > 10_000_000) errors.push(`large file needs review: ${rel}`);
  if (file.endsWith('.meta')) {
    const match = /^guid: ([0-9a-f]{32})$/m.exec(fs.readFileSync(file, 'utf8'));
    if (!match) errors.push(`invalid GUID: ${rel}`);
    else if (guids.has(match[1])) errors.push(`duplicate GUID: ${rel} and ${guids.get(match[1])}`);
    else guids.set(match[1], rel);
  }
  if (/\.(?:cs|py|json|md|uxml|uss|ps1)$/.test(rel)) {
    const body = fs.readFileSync(file, 'utf8');
    if (/ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|-----BEGIN (?:RSA |OPENSSH |EC )?PRIVATE KEY-----/.test(body))
      errors.push(`possible credential in ${rel}`);
  }
}
for (const error of errors) console.error(`FAIL: ${error}`);
console.log(`Checked package: ${guids.size} GUIDs, ${files.length} files`);
process.exitCode = errors.length ? 1 : 0;
