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
