// SPDX-License-Identifier: AGPL-3.0-or-later
//
// The npm audit gate must fail when the audit did not happen, not only when it found something.
// `npm audit --json` that cannot reach the registry prints an error object and no advisory list,
// and a gate that read an empty list from it passed. The script is run here with a stand-in `npm`
// first on PATH, so what is proved is the exit status CI sees.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';

import { judgeNpmAudit } from './npm-audit.mjs';

const script = join(dirname(fileURLToPath(import.meta.url)), 'npm-audit.mjs');

const SKIP_ON_WINDOWS =
  process.platform === 'win32' && 'the stand-in npm is an extensionless shell script found through PATH, which Windows does not run';

const CLEAN = { auditReportVersion: 2, vulnerabilities: {}, metadata: { vulnerabilities: { total: 0 } } };
const HIGH = {
  auditReportVersion: 2,
  vulnerabilities: {
    'left-pad': { severity: 'high', range: '<1.0.1', via: [{ url: 'https://github.com/advisories/GHSA-test-0000-0000' }] },
  },
  metadata: { vulnerabilities: { high: 1, total: 1 } },
};
const OFFLINE = { error: { code: 'ENOTFOUND', summary: 'request to https://registry.npmjs.org failed' } };

/** Runs the real script in a scratch repository root whose `npm` answers `stdout` and exits `status`. */
function runGate(report, status, allow = []) {
  const root = mkdtempSync(join(tmpdir(), 'silexgis-npm-audit-'));
  try {
    const bin = join(root, 'bin');
    mkdirSync(bin);
    mkdirSync(join(root, 'client'));
    writeFileSync(join(root, 'client', '.audit-allow.json'), JSON.stringify(allow));
    writeFileSync(join(root, 'report.json'), typeof report === 'string' ? report : JSON.stringify(report));
    writeFileSync(join(bin, 'npm'), `#!/usr/bin/env sh\ncat "${join(root, 'report.json')}"\nexit ${status}\n`);
    chmodSync(join(bin, 'npm'), 0o755);
    return spawnSync(process.execPath, [script], {
      cwd: root,
      encoding: 'utf8',
      env: { ...process.env, PATH: `${bin}:${process.env.PATH}` },
    });
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

describe('npm-audit.mjs, run with a stand-in npm', { skip: SKIP_ON_WINDOWS }, () => {
  it('passes a clean report and fails an unallowed high advisory', () => {
    assert.equal(runGate(CLEAN, 0).status, 0);
    const high = runGate(HIGH, 1);
    assert.equal(high.status, 1);
    assert.match(high.stderr, /left-pad/);
  });

  it('fails when npm could not reach the registry', () => {
    const r = runGate(OFFLINE, 1);
    assert.equal(r.status, 1, r.stdout);
    assert.match(r.stderr, /could not run: ENOTFOUND/);
    assert.doesNotMatch(r.stdout, /No high or critical/);
  });
});

describe('judgeNpmAudit', () => {
  const today = '2026-11-02';

  it('calls a report without an advisory list, or an unexpected exit, broken', () => {
    assert.match(judgeNpmAudit({ status: 0, stdout: '{}' }, [], today).broken, /no advisory list/);
    assert.match(judgeNpmAudit({ status: 0, stdout: 'not json' }, [], today).broken, /no JSON/);
    assert.match(judgeNpmAudit({ status: 254, stdout: JSON.stringify(CLEAN) }, [], today).broken, /exited 254/);
    assert.deepEqual(judgeNpmAudit({ status: 0, stdout: JSON.stringify(CLEAN) }, [], today), { printed: [], failing: [] });
  });

  it('allows an advisory only by an entry that names an expiry still ahead', () => {
    const run = (allow) => judgeNpmAudit({ status: 1, stdout: JSON.stringify(HIGH) }, allow, today).failing.length;
    assert.equal(run([{ package: 'left-pad', reason: 'test', until: '2026-12-01' }]), 0);
    assert.equal(run([{ package: 'left-pad', reason: 'test', until: '2026-11-01' }]), 1, 'an expired entry allows nothing');
    assert.equal(run([{ package: 'left-pad', reason: 'test' }]), 1, 'an entry without an expiry allows nothing');
  });
});
