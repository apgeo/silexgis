// SPDX-License-Identifier: AGPL-3.0-or-later

// One integration run at a time, machine-wide.
//
// The API integration suite is the machine's long pole, and two of them running at once do not
// share the box — they poison each other: load multiplies, the shared PostGIS container serialises
// everything against one database, and a suite that passes alone fails wholesale. The failures that
// come out of a loaded machine are timeouts and 500s, which read exactly like defects; more than one
// session has gone hunting a regression that was only contention.
//
// The lock is therefore machine-wide, and "machine-wide" is a claim about a shared *location and
// representation*, not about intent. Every worktree on this box must agree on both, or each takes a
// lock nobody else can see and the guarantee is worth nothing while still reporting success. Two
// facts are load-bearing and must not be changed on one side only:
//
//   * the lock is the single file <dir>/gate.lock, created with the 'wx' flag so that two processes
//     racing cannot both win, holding JSON with the holder's pid;
//   * the directory is /srv/data/silexgis, deliberately NOT the system temp dir — /tmp here is a
//     16 GB tmpfs, and filling it breaks shell output capture in ways that look like everything
//     else being broken.
//
// Staleness is decided by asking whether the recorded process still exists, so nothing depends on
// the file being cleared at boot.
//
// Fairness: waiters queue on numbered tickets in <dir>/queue and may only claim the lock while
// holding the oldest live ticket. Without this, whoever polls first after a release wins, and a
// politely waiting run can be overtaken indefinitely by worktrees that arrived later. Tickets whose
// process is gone are pruned, so a killed waiter cannot block the queue behind it.
//
// Usage, from anywhere:
//   node scripts/gate-lock.mjs status
//   node scripts/gate-lock.mjs acquire [--no-wait] [--label <text>]
//   node scripts/gate-lock.mjs release [--force]
//   node scripts/gate-lock.mjs run [--label <text>] [--result <file>] -- <command> [args...]
//
// `run` is the normal form: take the lock (waiting in line by default), run the command with
// inherited stdio, write a small JSON result file when it ends (so a detached caller can collect the
// verdict later), release, and exit with the command's exit code. `acquire` with --no-wait exits 3
// when the lock is held, so scripts can branch without parsing output. `--holder` is accepted as a
// synonym of `--label` so either spelling in circulation on this machine works.

import { mkdirSync, readdirSync, rmSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { spawn } from 'node:child_process';
import { hostname } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import process from 'node:process';

export function lockDir() {
  return process.env.SILEXGIS_GATE_LOCK_DIR || join('/srv', 'data', 'silexgis');
}

function lockFile(dir) {
  return join(dir, 'gate.lock');
}

export function readOwner(dir) {
  try {
    return JSON.parse(readFileSync(lockFile(dir), 'utf8'));
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

export function describe(owner) {
  const held = Math.round((Date.now() - (owner.acquiredAt ?? Date.now())) / 60000);
  return `${owner.holder || 'unlabelled'} (pid ${owner.pid}, ${owner.worktree || 'unknown worktree'}) — held ${held} min`;
}

/**
 * Try once to take the lock. Returns null when taken, and the blocking owner when genuinely held.
 * A lock whose recorded holder is no longer running is stale and is taken over; losing the race to
 * create the file just means this caller waits another round.
 */
export function tryAcquire(dir, holder) {
  mkdirSync(dir, { recursive: true });
  const existing = readOwner(dir);
  if (existing && pidAlive(existing.pid)) return existing;
  if (existing) rmSync(lockFile(dir), { force: true });
  try {
    writeFileSync(
      lockFile(dir),
      JSON.stringify(
        { holder: holder || `pid ${process.pid}`, pid: process.pid, worktree: process.cwd(), acquiredAt: Date.now() },
        null,
        2,
      ),
      { flag: 'wx' },
    );
    return null;
  } catch {
    return readOwner(dir);
  }
}

export function release(dir, { force = false } = {}) {
  const owner = readOwner(dir);
  if (!owner) return;
  if (!force && owner.pid !== process.pid && pidAlive(owner.pid)) {
    console.error(`gate-lock: refusing to release a live lock held by ${describe(owner)}; use --force if you are sure`);
    process.exit(2);
  }
  rmSync(lockFile(dir), { force: true });
}

function ticketDir(dir) {
  const d = join(dir, 'queue');
  mkdirSync(d, { recursive: true });
  return d;
}

function liveTickets(dir) {
  const d = ticketDir(dir);
  const tickets = [];
  for (const name of readdirSync(d)) {
    const file = join(d, name);
    let ticket;
    try {
      ticket = JSON.parse(readFileSync(file, 'utf8'));
    } catch {
      continue;
    }
    if (pidAlive(ticket.pid)) tickets.push({ ...ticket, file });
    else rmSync(file, { force: true });
  }
  // Ordered by when each waiter joined, so "whose turn is it" has the same answer in every process
  // that asks. The pid breaks a same-millisecond tie.
  return tickets.sort((a, b) => a.joinedAt - b.joinedAt || a.pid - b.pid);
}

async function acquireWaiting(dir, holder) {
  const file = join(ticketDir(dir), `${Date.now()}-${process.pid}.json`);
  writeFileSync(file, JSON.stringify({ holder: holder || `pid ${process.pid}`, pid: process.pid, joinedAt: Date.now() }), 'utf8');
  const dropTicket = () => rmSync(file, { force: true });
  // A waiter killed while queued must not leave its ticket behind blocking the rest.
  for (const sig of ['SIGINT', 'SIGTERM', 'SIGHUP']) process.once(sig, () => { dropTicket(); process.exit(1); });
  process.once('exit', dropTicket);

  let announced = null;
  for (;;) {
    const queue = liveTickets(dir);
    const ourTurn = queue.length === 0 || queue[0].pid === process.pid;
    const blocker = ourTurn
      ? tryAcquire(dir, holder)
      : readOwner(dir) ?? { holder: queue[0].holder, pid: queue[0].pid, worktree: 'queued', acquiredAt: queue[0].joinedAt };
    if (ourTurn && !blocker) {
      dropTicket();
      return;
    }
    const place = queue.findIndex((t) => t.pid === process.pid);
    const line = `${describe(blocker)}${place > 0 ? ` — ${place} ahead of us in the queue` : ''}`;
    if (line !== announced) {
      console.error(`gate-lock: waiting for ${line}`);
      announced = line;
    }
    await new Promise((r) => setTimeout(r, 15_000));
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
  const takeHolder = () => take('--label') ?? take('--holder');

  const dir = lockDir();

  if (cmd === 'status') {
    const o = existsSync(lockFile(dir)) ? readOwner(dir) : null;
    if (!o) console.log('free');
    else console.log(`held by ${describe(o)}` + (pidAlive(o.pid) ? '' : '  [STALE — holder is gone]'));
    const queue = existsSync(join(dir, 'queue')) ? liveTickets(dir) : [];
    if (queue.length) {
      console.log(`${queue.length} waiting, in turn order:`);
      for (const [i, t] of queue.entries()) {
        console.log(`  ${i + 1}. ${t.holder} (pid ${t.pid}, waiting ${Math.round((Date.now() - t.joinedAt) / 60000)} min)`);
      }
    }
    return;
  }

  if (cmd === 'acquire') {
    const holder = takeHolder();
    if (has('--no-wait')) {
      const blocker = tryAcquire(dir, holder);
      if (blocker) {
        console.error(`held by ${describe(blocker)}`);
        process.exit(3);
      }
    } else {
      await acquireWaiting(dir, holder);
    }
    console.log('acquired');
    return;
  }

  if (cmd === 'release') {
    release(dir, { force: has('--force') });
    console.log('released');
    return;
  }

  if (cmd === 'run') {
    const holder = takeHolder();
    const resultFile = take('--result');
    const sep = args.indexOf('--');
    if (sep === -1 || sep === args.length - 1) {
      console.error('run needs a command after --');
      process.exit(1);
    }
    const command = args.slice(sep + 1);

    await acquireWaiting(dir, holder);
    const startedAt = new Date();
    const child = spawn(command[0], command.slice(1), { stdio: 'inherit' });

    // Release on every way out, including the signals a session or a task runner sends. A gate that
    // leaves its lock behind when interrupted is worse than no gate: the next run waits for a
    // process that is not there, and the fix — forcing the lock — trains people to force it a habit.
    const finish = (code) => {
      if (resultFile) {
        const endedAt = new Date();
        writeFileSync(
          resultFile,
          JSON.stringify(
            {
              label: holder || '',
              command: command.join(' '),
              exitCode: code,
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
      process.exit(code);
    };

    child.on('close', (code, signal) => finish(code ?? (signal ? 128 : 1)));
    child.on('error', (e) => {
      console.error(`gate-lock: could not start the command: ${e.message}`);
      finish(127);
    });
    for (const sig of ['SIGINT', 'SIGTERM', 'SIGHUP']) process.on(sig, () => child.kill(sig));
    return;
  }

  console.error('usage: gate-lock.mjs status | acquire [--no-wait] [--label X] | release [--force] | run [--label X] [--result F] -- cmd...');
  process.exit(1);
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  main().catch((e) => {
    console.error(e.message);
    process.exit(1);
  });
}
