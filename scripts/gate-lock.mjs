// SPDX-License-Identifier: AGPL-3.0-or-later

// One integration run at a time, machine-wide.
//
// The API integration suite is the machine's long pole, and two of them running at once do
// not share the box — they poison each other: file-watcher handles run out, load quadruples,
// and a suite that passes alone fails wholesale. Every worktree therefore takes this lock
// before a full or targeted integration run, and queued runs execute in turn instead of
// concurrently. The lock is a directory created atomically; the holder records itself inside
// it, and a holder whose process is gone is stale and may be taken over.
//
// Usage, from anywhere:
//   node scripts/gate-lock.mjs status
//   node scripts/gate-lock.mjs acquire [--no-wait] [--label <text>]
//   node scripts/gate-lock.mjs release
//   node scripts/gate-lock.mjs run [--label <text>] [--result <file>] -- <command> [args...]
//
// `run` is the normal form: take the lock (waiting in line by default), run the command with
// inherited stdio, write a small JSON result file when it ends (so a detached caller can
// collect the verdict later), release, and exit with the command's exit code. `acquire` with
// --no-wait exits 3 when the lock is held, so scripts can branch without parsing output.
//
// The lock directory defaults to the system temp dir and can be pointed elsewhere with
// SILEXGIS_GATE_LOCK_DIR — it must name the same place for every worktree that shares the
// machine, which the default already does.

import { mkdirSync, rmSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { spawn } from 'node:child_process';
import { hostname, tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import process from 'node:process';

export function lockDir() {
  return process.env.SILEXGIS_GATE_LOCK_DIR || join(tmpdir(), 'silexgis-gate-lock');
}

function ownerFile(dir) {
  return join(dir, 'owner.json');
}

export function readOwner(dir) {
  try {
    return JSON.parse(readFileSync(ownerFile(dir), 'utf8'));
  } catch {
    return null;
  }
}

function pidAlive(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch (e) {
    // EPERM means the process exists but is not ours — still alive.
    return e.code === 'EPERM';
  }
}

/**
 * Try once to take the lock. Returns true when taken, false when genuinely held.
 * A directory whose recorded holder is no longer running is stale and is taken over;
 * a directory with no readable owner yet is a holder mid-write and counts as held.
 */
export function tryAcquire(dir, label) {
  try {
    mkdirSync(dir, { recursive: false });
  } catch (e) {
    if (e.code !== 'EEXIST') throw e;
    const owner = readOwner(dir);
    if (owner && !pidAlive(owner.pid)) {
      rmSync(dir, { recursive: true, force: true });
      return tryAcquire(dir, label);
    }
    return false;
  }
  writeFileSync(
    ownerFile(dir),
    JSON.stringify(
      { pid: process.pid, host: hostname(), label: label || '', since: new Date().toISOString() },
      null,
      2,
    ),
  );
  return true;
}

/**
 * Give up the lock — but only when it is ours to give up.
 *
 * Deleting it unconditionally looks harmless and is not. A caller that releases a lock it does
 * not hold destroys a running suite's claim, and the next waiter immediately starts a second
 * suite against the same machine: the exact collision this file exists to prevent. It also
 * cascades, because the dispossessed run releases again when it finishes and carries off the
 * new holder's claim in turn. Observed 2026-09-04, from a single hand-typed release.
 *
 * A holder that is gone, or a directory with no readable owner, is not a claim anybody is
 * relying on, so both are still removed — otherwise a crashed run would wedge the machine.
 * Returns true when the lock is now free.
 */
export function release(dir, { heldByPid = process.pid } = {}) {
  const owner = readOwner(dir);
  if (owner && owner.pid !== heldByPid && pidAlive(owner.pid)) return false;
  rmSync(dir, { recursive: true, force: true });
  return true;
}

async function acquireWaiting(dir, label) {
  let lastReport = 0;
  for (;;) {
    if (tryAcquire(dir, label)) return;
    const now = Date.now();
    if (now - lastReport > 60_000) {
      const o = readOwner(dir);
      console.error(
        o
          ? `waiting for the gate lock, held by pid ${o.pid} (${o.label || 'unlabelled'}) since ${o.since}`
          : 'waiting for the gate lock',
      );
      lastReport = now;
    }
    await new Promise((r) => setTimeout(r, 5000));
  }
}

async function main() {
  const args = process.argv.slice(2);
  const cmd = args.shift();
  const take = (flag) => {
    const i = args.indexOf(flag);
    if (i === -1) return undefined;
    const v = args[i + 1];
    args.splice(i, 2);
    return v;
  };
  const has = (flag) => {
    const i = args.indexOf(flag);
    if (i === -1) return false;
    args.splice(i, 1);
    return true;
  };

  const dir = lockDir();

  if (cmd === 'status') {
    const o = existsSync(dir) ? readOwner(dir) : null;
    if (!o) {
      console.log('free');
    } else {
      console.log(
        `held by pid ${o.pid} (${o.label || 'unlabelled'}) on ${o.host} since ${o.since}` +
          (pidAlive(o.pid) ? '' : '  [STALE — holder is gone]'),
      );
    }
    return;
  }

  if (cmd === 'acquire') {
    const label = take('--label');
    if (has('--no-wait')) {
      if (!tryAcquire(dir, label)) {
        const o = readOwner(dir);
        console.error(`held by pid ${o?.pid} (${o?.label || 'unlabelled'}) since ${o?.since}`);
        process.exit(3);
      }
    } else {
      await acquireWaiting(dir, label);
    }
    console.log('acquired');
    return;
  }

  if (cmd === 'release') {
    if (release(dir)) {
      console.log('released');
      return;
    }
    const o = readOwner(dir);
    console.error(
      `refusing: the lock is held by pid ${o.pid} (${o.label || 'unlabelled'}) since ${o.since}, ` +
        'which is still running. Wait for it, or stop that run deliberately.',
    );
    process.exit(3);
  }

  if (cmd === 'run') {
    const label = take('--label');
    const resultFile = take('--result');
    const sep = args.indexOf('--');
    if (sep === -1 || sep === args.length - 1) {
      console.error('run needs a command after --');
      process.exit(1);
    }
    const command = args.slice(sep + 1);

    await acquireWaiting(dir, label);
    const startedAt = new Date();
    const child = spawn(command[0], command.slice(1), { stdio: 'inherit' });
    const forward = (sig) => child.kill(sig);
    process.on('SIGINT', forward);
    process.on('SIGTERM', forward);

    const exitCode = await new Promise((resolve) => {
      child.on('close', (code, signal) => resolve(code ?? (signal ? 128 : 1)));
      child.on('error', (e) => {
        console.error(e.message);
        resolve(127);
      });
    });

    const endedAt = new Date();
    if (resultFile) {
      writeFileSync(
        resultFile,
        JSON.stringify(
          {
            label: label || '',
            command: command.join(' '),
            exitCode,
            startedAt: startedAt.toISOString(),
            endedAt: endedAt.toISOString(),
            durationSeconds: Math.round((endedAt - startedAt) / 1000),
            host: hostname(),
          },
          null,
          2,
        ),
      );
    }
    release(dir);
    process.exit(exitCode);
  }

  console.error('usage: gate-lock.mjs status | acquire [--no-wait] [--label X] | release | run [--label X] [--result F] -- cmd...');
  process.exit(1);
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  main().catch((e) => {
    console.error(e.message);
    process.exit(1);
  });
}
