// SPDX-License-Identifier: AGPL-3.0-or-later

// Fails on high and critical npm advisories that are not allowed, by name, in
// client/.audit-allow.json. An allow entry says which advisory, why it is tolerated and until
// when; an expired entry counts as not allowed, so a tolerated finding is re-judged rather than
// forgotten. Moderate and low findings are printed and do not fail the run — they arrive weekly
// through Dependabot regardless. Run from the repository root.

import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import process from 'node:process';

const client = join(process.cwd(), 'client');
const allowFile = join(client, '.audit-allow.json');
const allowed = existsSync(allowFile) ? JSON.parse(readFileSync(allowFile, 'utf8')) : [];
const today = new Date().toISOString().slice(0, 10);

const r = spawnSync('npm', ['audit', '--json'], {
  cwd: client,
  encoding: 'utf8',
  shell: process.platform === 'win32',
  maxBuffer: 64 * 1024 * 1024,
});
let report;
try {
  report = JSON.parse(r.stdout);
} catch {
  console.error('npm audit produced no JSON report');
  process.stderr.write(r.stderr || '');
  process.exit(1);
}

const failing = [];
for (const [name, v] of Object.entries(report.vulnerabilities ?? {})) {
  const advisories = (v.via ?? []).filter((x) => typeof x === 'object');
  const ids = advisories.map((a) => a.url?.split('/').pop() ?? a.title);
  const line = `${v.severity.padEnd(8)} ${name} ${v.range ?? ''} ${ids.join(', ')}`;
  if (!['high', 'critical'].includes(v.severity)) {
    console.log(`  ${line}`);
    continue;
  }
  const allow = allowed.find((a) => a.package === name && (a.until ?? '9999-12-31') >= today);
  if (allow) {
    console.log(`  ${line}  [allowed until ${allow.until}: ${allow.reason}]`);
  } else {
    failing.push(line);
  }
}
if (failing.length) {
  console.error('\nHigh or critical advisories not on the allow-list:\n  ' + failing.join('\n  '));
  console.error(`\nFix them, or add an entry with a reason and an expiry to ${allowFile}.`);
  process.exit(1);
}
console.log('\nNo high or critical npm advisory outside the allow-list.');
