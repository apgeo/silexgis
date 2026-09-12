// SPDX-License-Identifier: AGPL-3.0-or-later

// Tests for the gate lock: taking it, queueing behind it, taking over a holder that died,
// and the `run` form releasing it and recording a verdict whatever the command's outcome.
//
// Run from the repository root with `node --test "scripts/**/*.test.mjs"`. Each test points
// SILEXGIS_GATE_LOCK_DIR at its own temporary directory, so nothing here can collide with a
// real integration run on the same machine.

import { strict as assert } from 'node:assert';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync, mkdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { after, describe, it } from 'node:test';

import { tryAcquire, release, readOwner } from './gate-lock.mjs';

const script = join(dirname(fileURLToPath(import.meta.url)), 'gate-lock.mjs');
const scratch = mkdtempSync(join(tmpdir(), 'gate-lock-test-'));
after(() => rmSync(scratch, { recursive: true, force: true }));

const freshDir = (name) => join(scratch, name);

describe('acquire and release', () => {
  it('a free lock is taken, a held lock is not, a released lock is free again', () => {
    const dir = freshDir('basic');
    assert.equal(tryAcquire(dir, 'first'), true);
    assert.equal(readOwner(dir).label, 'first');
    assert.equal(tryAcquire(dir, 'second'), false);
    release(dir);
    assert.equal(tryAcquire(dir, 'second'), true);
    release(dir);
  });

  it('a holder whose process is gone is stale and is taken over', () => {
    const dir = freshDir('stale');
    // A process that has already exited supplies a pid that is certainly not running.
    const dead = spawnSync(process.execPath, ['-e', '']);
    mkdirSync(dir);
    writeFileSync(
      join(dir, 'owner.json'),
      JSON.stringify({ pid: dead.pid, host: 'gone', label: 'crashed run', since: '2026-01-01T00:00:00Z' }),
    );
    assert.equal(tryAcquire(dir, 'successor'), true);
    assert.equal(readOwner(dir).label, 'successor');
    release(dir);
  });

  it('the command line refuses a held lock with exit 3 under --no-wait', () => {
    const dir = freshDir('cli');
    assert.equal(tryAcquire(dir, 'holder'), true);
    const r = spawnSync(process.execPath, [script, 'acquire', '--no-wait', '--label', 'late'], {
      env: { ...process.env, SILEXGIS_GATE_LOCK_DIR: dir },
      encoding: 'utf8',
    });
    assert.equal(r.status, 3);
    release(dir);
  });
});

describe('run', () => {
  it('runs the command, records the verdict, propagates the exit code, and releases', () => {
    const dir = freshDir('run');
    const result = join(scratch, 'result.json');
    const r = spawnSync(
      process.execPath,
      [script, 'run', '--label', 'probe', '--result', result, '--', process.execPath, '-e', 'process.exit(3)'],
      { env: { ...process.env, SILEXGIS_GATE_LOCK_DIR: dir }, encoding: 'utf8' },
    );
    assert.equal(r.status, 3);
    const verdict = JSON.parse(readFileSync(result, 'utf8'));
    assert.equal(verdict.exitCode, 3);
    assert.equal(verdict.label, 'probe');
    assert.ok(verdict.startedAt <= verdict.endedAt);
    assert.equal(existsSync(dir), false, 'the lock must be released after the run');
  });

  it('a green command reports exit 0 the same way', () => {
    const dir = freshDir('run-green');
    const result = join(scratch, 'result-green.json');
    const r = spawnSync(
      process.execPath,
      [script, 'run', '--result', result, '--', process.execPath, '-e', ''],
      { env: { ...process.env, SILEXGIS_GATE_LOCK_DIR: dir }, encoding: 'utf8' },
    );
    assert.equal(r.status, 0);
    assert.equal(JSON.parse(readFileSync(result, 'utf8')).exitCode, 0);
    assert.equal(existsSync(dir), false);
  });
});

// An exit code cannot tell a passing run from one that ran nothing, and both have been collected
// here as verdicts: a targeted run whose thirty-six tests all failed to reach the database wrote
// exitCode 0, and a filter matching no class wrote the same. The result file now carries what the
// runner said it executed, and a verdict that cannot read as green without it.
describe('a result file says what actually ran', () => {
  const runWith = (name, output) => {
    const dir = freshDir(`verdict-${name}`);
    const result = join(scratch, `result-${name}.json`);
    spawnSync(
      process.execPath,
      [script, 'run', '--result', result, '--', process.execPath, '-e', `console.log(${JSON.stringify(output)})`],
      { env: { ...process.env, SILEXGIS_GATE_LOCK_DIR: dir }, encoding: 'utf8' },
    );
    return JSON.parse(readFileSync(result, 'utf8'));
  };

  it('a run whose tests all failed is not green, whatever it exited', () => {
    const r = runWith(
      'allfailed',
      'Failed!  - Failed:    36, Passed:     0, Skipped:     0, Total:    36, Duration: 9 s - A.dll',
    );
    assert.equal(r.exitCode, 0, 'the child deliberately exits 0 — this is the false-green case');
    assert.equal(r.verdict, 'red');
    assert.equal(r.failed, 36);
  });

  it('a filter that matched nothing is inconclusive, not green', () => {
    const r = runWith(
      'nothing',
      'Passed!  - Failed:     0, Passed:     0, Skipped:     0, Total:     0, Duration: 1 ms - A.dll',
    );
    assert.equal(r.verdict, 'inconclusive');
    assert.equal(r.total, 0);
  });

  it('a run with no summary at all is inconclusive and says so', () => {
    const r = runWith('nosummary', 'build noise and nothing else');
    assert.equal(r.verdict, 'inconclusive');
    assert.equal(r.summarySeen, false);
    assert.match(r.verdictReason, /no test-runner summary/);
  });

  it('a genuinely green run is green, and its counts are the runner\'s', () => {
    const r = runWith(
      'green',
      'Passed!  - Failed:     0, Passed:    33, Skipped:     2, Total:    35, Duration: 1 s - A.dll',
    );
    assert.equal(r.verdict, 'green');
    assert.equal(r.total, 35);
    assert.equal(r.passed, 33);
    assert.equal(r.skipped, 2);
  });

  it('several assemblies are summed rather than the last one winning', () => {
    const r = runWith(
      'multi',
      'Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10, Duration: 1 s - A.dll\n'
        + 'Passed!  - Failed:     0, Passed:     5, Skipped:     1, Total:     6, Duration: 1 s - B.dll',
    );
    assert.equal(r.assemblies, 2);
    assert.equal(r.total, 16);
    assert.equal(r.verdict, 'green');
  });
});
