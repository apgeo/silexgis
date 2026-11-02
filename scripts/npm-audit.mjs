// SPDX-License-Identifier: AGPL-3.0-or-later

// Fails on high and critical npm advisories that are not allowed, by name, in
// client/.audit-allow.json. An allow entry says which advisory, why it is tolerated and until
// when; an expired entry counts as not allowed, so a tolerated finding is re-judged rather than
// forgotten, and an entry without an expiry date allows nothing. Moderate and low findings are
// printed and do not fail the run — they arrive weekly through Dependabot regardless. Run from the
// repository root.
//
// The run also fails when the audit itself did not happen. `npm audit --json` that cannot reach
// the registry still prints JSON — an `error` object and no `vulnerabilities` — and an empty list
// read from it would pass as "no advisory"; so a report must carry the advisory list and the
// metadata npm writes beside it, must hold no error, and npm must have exited 0 (nothing found) or
// 1 (something found).

import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import process from 'node:process';

/**
 * Judges one `npm audit --json` run. Returns `{ broken }` with the reason when the audit did not
 * produce a usable report, otherwise `{ printed, failing }`: the lines to print and the lines that
 * fail the run.
 */
export function judgeNpmAudit({ status, stdout }, allowed, today) {
  let report;
  try {
    report = JSON.parse(stdout);
  } catch {
    return { broken: 'npm audit produced no JSON report' };
  }
  if (report?.error) {
    const e = report.error;
    return { broken: `npm audit could not run: ${[e.code, e.summary ?? e.message].filter(Boolean).join(' ')}` };
  }
  if (status !== 0 && status !== 1) return { broken: `npm audit exited ${status}` };
  if (typeof report?.vulnerabilities !== 'object' || report.vulnerabilities === null || typeof report.metadata !== 'object') {
    return { broken: 'npm audit wrote a report with no advisory list in it' };
  }

  const printed = [];
  const failing = [];
  for (const [name, v] of Object.entries(report.vulnerabilities)) {
    const advisories = (v.via ?? []).filter((x) => typeof x === 'object');
    const ids = advisories.map((a) => a.url?.split('/').pop() ?? a.title);
    const line = `${String(v.severity).padEnd(8)} ${name} ${v.range ?? ''} ${ids.join(', ')}`;
    if (!['high', 'critical'].includes(v.severity)) {
      printed.push(`  ${line}`);
      continue;
    }
    const allow = allowed.find((a) => a.package === name && typeof a.until === 'string' && a.until >= today);
    if (allow) printed.push(`  ${line}  [allowed until ${allow.until}: ${allow.reason}]`);
    else failing.push(line);
  }
  return { printed, failing };
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
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
  if (r.error) {
    console.error(`npm audit could not be started: ${r.error.message}`);
    process.exit(1);
  }
  const verdict = judgeNpmAudit({ status: r.status, stdout: r.stdout ?? '' }, allowed, today);
  if (verdict.broken) {
    console.error(`${verdict.broken} — no audit was done, so this run proves nothing either way.`);
    process.stderr.write(r.stderr || '');
    process.exit(1);
  }
  for (const line of verdict.printed) console.log(line);
  if (verdict.failing.length) {
    console.error('\nHigh or critical advisories not on the allow-list:\n  ' + verdict.failing.join('\n  '));
    console.error(`\nFix them, or add an entry with a reason and an expiry to ${allowFile}.`);
    process.exit(1);
  }
  console.log('\nNo high or critical npm advisory outside the allow-list.');
}
