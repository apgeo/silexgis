// SPDX-License-Identifier: AGPL-3.0-or-later

// One integration run at a time, machine-wide.
//
// The API integration suite is the machine's long pole, and two of them running at once do
// not share the box — they poison each other: file-watcher handles run out, load quadruples,
// and a suite that passes alone fails wholesale. Every worktree therefore takes this lock
// before a full or targeted integration run, and queued runs execute in turn instead of
// concurrently.
//
// What the lock IS matters as much as that it exists, because a lock two programs disagree
// about is not a lock: two copies of this tool once locked two different places, each reported
// `free` while the other was held, and two full suites ran side by side for three hours. So:
//
//   * the lock is ONE NAMED FILE, <dir>/gate.lock, created with the exclusive-create flag so two
//     processes racing cannot both win, holding the holder's pid, label and worktree as JSON;
//   * <dir> is the persistent data directory (/srv/data/silexgis where it exists), deliberately
//     NOT the system temporary directory — on the development machine that is a 16 GB tmpfs,
//     i.e. memory, and a lock that lives in memory is also one that vanishes on reboot while the
//     tool still believes in it; SILEXGIS_GATE_LOCK_DIR overrides it, and must then name the
//     same place for every worktree on the machine;
//   * a holder whose process is gone is stale and is taken over; a holder whose process is alive
//     but has burned no CPU for a quarter of an hour is wedged — twice this machine sat for eight
//     and twelve hours behind one — and the next waiter takes over from it, loudly, leaving the
//     process itself alone;
//   * waiters queue on numbered tickets in <dir>/queue and may only claim the lock while holding
//     the oldest live ticket, so a run that has waited longest goes next instead of whichever
//     process happened to poll first after a release.
//
// Changeover: older copies of this tool, still present in older worktrees, lock a DIRECTORY
// under the system temporary directory. A suite one of them started is still a suite, so that
// location is read — never written — and honoured while its holder lives. Once the last of those
// copies is gone this paragraph and `legacyBlocker` can go with it.
//
// Usage, from anywhere:
//   node scripts/gate-lock.mjs status [--probe]
//   node scripts/gate-lock.mjs acquire [--no-wait] [--label <text>]
//   node scripts/gate-lock.mjs release
//   node scripts/gate-lock.mjs steal --pid <holder> [--force]
//   node scripts/gate-lock.mjs run [--label <text>] [--result <file>] [--full] -- <command> [args...]
//
// `run` is the normal form: take the lock (waiting in line by default), run the command with
// inherited stdio, write a small JSON result file when it ends (so a detached caller can
// collect the verdict later), release, and exit with the command's exit code. `acquire` with
// --no-wait exits 3 when the lock is held, so scripts can branch without parsing output.
//
// A `run` also fingerprints the test assemblies it is about to execute and re-checks them while
// it goes. If another worktree's build overwrites them mid-run the run is killed, the result file
// says `verdict: "void"`, and it exits 75 — because a verdict assembled from two builds proves
// nothing in either direction, and the failure is otherwise silent for as long as the suite takes.
//
// A `run` of the whole API integration suite — `dotnet test` naming the Api.Tests project with
// no --filter — is refused unless --full is given. Four consecutive batches started one by
// accident despite a brief forbidding it, three of them at once, which at the suite's length
// would have serialised more than a day of lock behind every other effort on the machine. An
// instruction did not stop that; a flag that has to be typed on purpose does.

import {
  existsSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  rmSync,
  statSync,
  writeFileSync,
} from 'node:fs';
import { spawn } from 'node:child_process';
import { homedir, hostname, tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import process from 'node:process';

/**
 * How often a run re-checks that nobody has rebuilt the assemblies it is executing. Overridable
 * only so the guard's own test can drive it; a minute is right for a suite measured in hours.
 */
const AssemblyCheckMs = Number(process.env.GATE_LOCK_ASSEMBLY_CHECK_MS || 60_000);
/** How long a live holder may burn no CPU at all before the next waiter takes over. */
const WedgedAfterMs = Number(process.env.GATE_LOCK_WEDGED_MS || 15 * 60_000);
/** The window over which a holder's CPU is sampled when a waiter looks at it. */
const ProbeWindowMs = Number(process.env.GATE_LOCK_PROBE_MS || 4_000);
/** How much of a run's output is kept to find the summary in. */
const OutputTailBytes = 64 * 1024;
const LockFileName = 'gate.lock';

/**
 * What a run actually executed, read off the runner's own summary line.
 *
 * An exit code alone cannot tell a passing run from one that ran nothing. Both of those have
 * happened here and both were collected as verdicts: a targeted run whose thirty-six tests all
 * failed to reach the database wrote `exitCode: 0`, and a filter that matched no class at all
 * wrote the same. A caller reading only the exit code calls both of them green, and the whole
 * point of writing a result file is that somebody reads it later instead of watching the run.
 *
 * So the counts go in beside the exit code, and a `verdict` that is only ever `green` when a
 * summary was actually seen, it reported tests, and none of them failed. Anything else is
 * `inconclusive` with the reason spelled out — which is a thing a reader can act on, unlike a
 * zero.
 *
 * The shape parsed is the .NET test runner's, because that is what the gate runs:
 *   `Passed!  - Failed:     0, Passed:    33, Skipped:     0, Total:    33, Duration: 1 s - X.dll`
 * A run producing several assemblies prints one such line each, and they are summed. Output this
 * does not recognise is reported as unrecognised rather than as zero tests, because "I could not
 * read this" and "nothing ran" are different facts and only one of them is the runner's fault.
 */
export function summarise(output) {
  const line = /^\s*(Passed|Failed|Skipped)!\s*-\s*Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)/gm;
  let failed = 0;
  let passed = 0;
  let skipped = 0;
  let total = 0;
  let seen = 0;

  for (const m of (output || '').matchAll(line)) {
    seen += 1;
    failed += Number(m[2]);
    passed += Number(m[3]);
    skipped += Number(m[4]);
    total += Number(m[5]);
  }

  if (seen === 0) {
    return {
      summarySeen: false,
      verdict: 'inconclusive',
      verdictReason: 'no test-runner summary appeared in the output, so nothing here says any test ran',
    };
  }

  const counts = { summarySeen: true, assemblies: seen, failed, passed, skipped, total };

  if (total === 0) {
    return {
      ...counts,
      verdict: 'inconclusive',
      verdictReason: 'the runner reported a summary of zero tests — a filter that matched nothing',
    };
  }

  if (failed > 0) {
    return { ...counts, verdict: 'red', verdictReason: `${failed} of ${total} failed` };
  }

  return { ...counts, verdict: 'green' };
}

/**
 * The directory the lock file and the ticket queue live in.
 *
 * The persistent data directory where the machine has one, and the user's home otherwise — both
 * survive a reboot, and neither is the temporary directory. The override exists so every copy of
 * this tool can be pointed at one place, and so the tests can point each case at a scratch one.
 */
export function lockDir() {
  if (process.env.SILEXGIS_GATE_LOCK_DIR) return process.env.SILEXGIS_GATE_LOCK_DIR;
  const data = '/srv/data/silexgis';
  return existsSync(data) ? data : join(homedir(), '.silexgis-gate');
}

export function lockFile(dir) {
  return join(dir, LockFileName);
}

function queueDir(dir) {
  return join(dir, 'queue');
}

export function readOwner(dir) {
  try {
    return JSON.parse(readFileSync(lockFile(dir), 'utf8'));
  } catch {
    return null;
  }
}

function pidAlive(pid) {
  if (!Number.isInteger(pid) || pid <= 0) return false;
  try {
    process.kill(pid, 0);
    return true;
  } catch (e) {
    // EPERM means the process exists but is not ours — still alive.
    return e.code === 'EPERM';
  }
}

/**
 * CPU seconds burned by a process and everything under it, or null where that cannot be read.
 *
 * Reads /proc directly rather than shelling out, and answers null on any platform without it —
 * the caller treats null as "cannot tell" and never as "not progressing", because a wrong
 * accusation here costs somebody their run.
 */
export function cpuSecondsOfTree(pid) {
  if (!existsSync('/proc')) return null;
  let total = 0;
  let found = false;
  const children = new Map();
  let entries;
  try {
    entries = readdirSync('/proc').filter((n) => /^\d+$/.test(n));
  } catch {
    return null;
  }
  const stats = new Map();
  for (const entry of entries) {
    let raw;
    try {
      raw = readFileSync(`/proc/${entry}/stat`, 'utf8');
    } catch {
      continue; // the process ended between listing and reading; nothing to account for
    }
    // The command field can itself contain spaces and brackets, so fields are counted from the
    // closing bracket rather than from the start of the line.
    const close = raw.lastIndexOf(')');
    if (close === -1) continue;
    const fields = raw.slice(close + 2).split(' ');
    const ppid = Number(fields[1]);
    const utime = Number(fields[11]);
    const stime = Number(fields[12]);
    if (!Number.isFinite(ppid) || !Number.isFinite(utime) || !Number.isFinite(stime)) continue;
    stats.set(Number(entry), { ppid, cpu: (utime + stime) / 100 });
    if (!children.has(ppid)) children.set(ppid, []);
    children.get(ppid).push(Number(entry));
  }
  const walk = (p) => {
    const self = stats.get(p);
    if (self) {
      total += self.cpu;
      found = true;
    }
    for (const child of children.get(p) || []) walk(child);
  };
  walk(pid);
  return found ? total : null;
}

/**
 * Whether the holder is doing anything, sampled over a window.
 *
 * A holder that is merely old is not a problem — a full suite legitimately runs for hours. A
 * holder burning no CPU at all is a different thing, and it is what wedges this machine: twice
 * observed sitting on the lock for eight and twelve hours at around two per cent of a core with a
 * hundred megabytes resident, while several other sessions queued behind it and the box idled.
 * Neither age nor the process still existing distinguishes the two, which is why this samples.
 */
export async function holderProgress(pid, { windowMs = ProbeWindowMs } = {}) {
  const before = cpuSecondsOfTree(pid);
  if (before === null) return { known: false };
  await new Promise((r) => setTimeout(r, windowMs));
  const after = cpuSecondsOfTree(pid);
  if (after === null) return { known: false };
  return { known: true, cpuSeconds: after - before, windowMs, total: after };
}

/**
 * The directory an older copy of this tool locks, read for the changeover and never written.
 * Overridable so the tests can stand a legacy lock up without touching the machine's real one.
 */
function legacyDir() {
  return process.env.SILEXGIS_GATE_LEGACY_DIR || join(tmpdir(), 'silexgis-gate-lock');
}

/**
 * A run started by an older copy of this tool, or null. A legacy holder whose process is gone is
 * cleared here, because nothing else will ever clear it.
 */
export function legacyBlocker() {
  const dir = legacyDir();
  if (!existsSync(dir)) return null;
  let owner = null;
  try {
    owner = JSON.parse(readFileSync(join(dir, 'owner.json'), 'utf8'));
  } catch {
    owner = null;
  }
  if (!owner || !pidAlive(owner.pid)) {
    rmSync(dir, { recursive: true, force: true });
    return null;
  }
  return {
    kind: 'legacy',
    pid: owner.pid,
    label: owner.label || 'unlabelled',
    since: owner.since,
    clear: () => rmSync(dir, { recursive: true, force: true }),
  };
}

/**
 * Whoever currently stands between a caller and the lock: the persistent holder, a legacy
 * holder, or null when the lock can be taken.
 */
export function blocker(dir) {
  const owner = readOwner(dir);
  if (owner && pidAlive(owner.pid)) {
    return {
      kind: 'persistent',
      pid: owner.pid,
      label: owner.label || 'unlabelled',
      since: owner.since,
      clear: () => rmSync(lockFile(dir), { force: true }),
    };
  }
  return legacyBlocker();
}

/**
 * Try once to take the lock. Returns true when taken, false when genuinely held.
 * A file whose recorded holder is no longer running is stale and is taken over; a file with no
 * readable owner is a holder mid-write and counts as held — unless it has sat unreadable for
 * longer than any write takes, in which case it is a corpse from an interrupted write.
 */
export function tryAcquire(dir, label, { cwd = process.cwd() } = {}) {
  mkdirSync(dir, { recursive: true });
  if (legacyBlocker()) return false;
  const file = lockFile(dir);
  const record = {
    pid: process.pid,
    host: hostname(),
    label: label || '',
    since: new Date().toISOString(),
    cwd,
  };
  try {
    writeFileSync(file, JSON.stringify(record, null, 2), { flag: 'wx' });
    return true;
  } catch (e) {
    if (e.code !== 'EEXIST') throw e;
    const owner = readOwner(dir);
    if (owner && !pidAlive(owner.pid)) {
      rmSync(file, { force: true });
      return tryAcquire(dir, label, { cwd });
    }
    if (!owner) {
      let ageMs = 0;
      try {
        ageMs = Date.now() - statSync(file).mtimeMs;
      } catch {
        return tryAcquire(dir, label, { cwd }); // vanished between the two reads: free now
      }
      if (ageMs > 30_000) {
        rmSync(file, { force: true });
        return tryAcquire(dir, label, { cwd });
      }
    }
    return false;
  }
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
 * A holder that is gone, or a file with no readable owner, is not a claim anybody is relying
 * on, so both are still removed — otherwise a crashed run would wedge the machine.
 * Returns true when the lock is now free.
 */
export function release(dir, { heldByPid = process.pid } = {}) {
  const owner = readOwner(dir);
  if (owner && owner.pid !== heldByPid && pidAlive(owner.pid)) return false;
  rmSync(lockFile(dir), { force: true });
  return true;
}

/** Joins the queue: a ticket whose name sorts by arrival, holding the waiter's pid. */
function takeTicket(dir, label) {
  const q = queueDir(dir);
  mkdirSync(q, { recursive: true });
  const name = `${String(Date.now()).padStart(15, '0')}-${String(process.pid).padStart(8, '0')}.json`;
  const file = join(q, name);
  writeFileSync(
    file,
    JSON.stringify({ pid: process.pid, label: label || '', since: new Date().toISOString() }),
  );
  return file;
}

/** Live tickets, oldest first. A ticket whose process is gone is pruned on the way past. */
export function liveTickets(dir) {
  const q = queueDir(dir);
  if (!existsSync(q)) return [];
  const live = [];
  for (const name of readdirSync(q).filter((f) => f.endsWith('.json')).sort()) {
    const file = join(q, name);
    let ticket;
    try {
      ticket = JSON.parse(readFileSync(file, 'utf8'));
    } catch {
      continue; // being written by its owner right now
    }
    if (pidAlive(ticket.pid)) live.push({ ...ticket, file });
    else rmSync(file, { force: true });
  }
  return live;
}

function describeBlocker(b) {
  const where = b.kind === 'legacy' ? ' [under the temporary directory, by an older copy of this tool]' : '';
  return `pid ${b.pid} (${b.label}) since ${b.since}${where}`;
}

/**
 * Wait for the lock, in turn. Returns once it is held.
 *
 * Two things decide whether waiting is worth anything, and both are said out loud once a minute
 * rather than repeating one line for hours: the holder's CPU, and the caller's place in the
 * queue. A holder that burns nothing for `WedgedAfterMs` is taken over here, on the spot — the
 * machine has sat idle behind one for twelve hours while four sessions waited politely.
 */
async function acquireWaiting(dir, label, { cwd = process.cwd() } = {}) {
  const ticket = takeTicket(dir, label);
  const dropTicket = () => rmSync(ticket, { force: true });
  process.on('exit', dropTicket);
  let lastReport = 0;
  let idleSince = null;
  let idleBlocker = null;
  try {
    for (;;) {
      const queue = liveTickets(dir);
      const myTurn = queue.length === 0 || queue[0].pid === process.pid;
      if (myTurn && tryAcquire(dir, label, { cwd })) return;

      const b = blocker(dir);
      const now = Date.now();
      let how = '';
      if (b && myTurn) {
        const p = await holderProgress(b.pid);
        if (p.known) {
          if (p.cpuSeconds < 0.05) {
            if (idleBlocker !== b.pid) {
              idleBlocker = b.pid;
              idleSince = now;
            }
            const idleFor = Date.now() - idleSince;
            how = ` — holder is burning no CPU (${Math.round(idleFor / 1000)}s so far; taken over at ${Math.round(WedgedAfterMs / 1000)}s)`;
            if (idleFor >= WedgedAfterMs) {
              console.error(
                `gate-lock: holder ${describeBlocker(b)} has burned no CPU for ${Math.round(idleFor / 60000)} min`
                  + ' — WEDGED; taking the lock over and leaving its process alone',
              );
              b.clear();
              idleBlocker = null;
              idleSince = null;
              continue;
            }
          } else {
            idleBlocker = null;
            idleSince = null;
            how = ` — holder is working (${p.cpuSeconds.toFixed(1)}s CPU in ${p.windowMs / 1000}s)`;
          }
        }
      }

      if (now - lastReport > 60_000) {
        const position = queue.findIndex((t) => t.pid === process.pid);
        const place = position > 0 ? `; ${position} ahead in the queue` : '';
        console.error(
          b
            ? `waiting for the gate lock, held by ${describeBlocker(b)}${how}${place}`
            : `waiting for the gate lock${place}`,
        );
        lastReport = now;
      }
      await new Promise((r) => setTimeout(r, 5000));
    }
  } finally {
    dropTicket();
    process.off('exit', dropTicket);
  }
}

/**
 * Whether a command is the whole API integration suite: `dotnet test` naming the Api.Tests
 * project or its directory, with nothing narrowing it. Purely lexical, on purpose — the point
 * is to refuse before anything runs.
 */
export function isUnfilteredApiSuite(command) {
  if (command.length < 2) return false;
  const tool = basename(command[0].replaceAll('\\', '/')).replace(/\.exe$/i, '');
  if (tool !== 'dotnet' || command[1] !== 'test') return false;
  const namesSuite = command.some((a) => /Api\.Tests(\.csproj)?$/.test(a.replaceAll('\\', '/').replace(/\/+$/, '')));
  if (!namesSuite) return false;
  const narrowed = command.some((a) => a === '--filter' || a.startsWith('--filter=') || a === '--list-tests' || a === '-t');
  return !narrowed;
}

/**
 * The assemblies a run is about to test, fingerprinted so nobody can swap them out from under it.
 *
 * Two full runs were destroyed this way before this existed, and neither reported anything wrong:
 * a run takes the lock, starts executing, and hours later another worktree's build rewrites the
 * very `.dll` files being executed. The run continues and produces a verdict that describes a
 * mixture of two builds — a red that proves no defect and a green that proves no absence. The tell
 * was only ever visible afterwards, by comparing the test *total* against neighbouring runs.
 *
 * So the fingerprint is taken at acquire time and re-checked while the run is in flight. What
 * matters is that it fails LOUDLY and EARLY: the failure mode this replaces was silent for
 * fifteen and twenty-four hours respectively, while every health probe reported a healthy,
 * hard-working suite.
 */
function outputDirsFor(command, cwd) {
  // `dotnet test <project file>` and `dotnet test <project directory>` are the two shapes every
  // caller uses; the assemblies live under that project's bin/. The directory shape used to fall
  // through to the working directory, which has no bin/ of its own, so a run started that way had
  // no guard at all. Anything else still falls back to the working directory, which over-collects
  // rather than under-collects — a false alarm costs a re-run, a miss costs a day.
  for (const a of command) {
    if (/\.(csproj|slnx|sln)$/i.test(a)) {
      const bin = join(dirname(resolve(cwd, a)), 'bin');
      if (existsSync(bin)) return [bin];
    }
  }
  for (const a of command.slice(2)) {
    const p = resolve(cwd, a);
    try {
      if (statSync(p).isDirectory() && readdirSync(p).some((f) => f.endsWith('.csproj'))) {
        const bin = join(p, 'bin');
        if (existsSync(bin)) return [bin];
      }
    } catch {
      /* not a path at all: a flag or a filter */
    }
  }
  const bin = join(cwd, 'bin');
  return existsSync(bin) ? [bin] : [];
}

function fingerprint(dirs) {
  const seen = [];
  const walk = (d) => {
    let entries;
    try {
      entries = readdirSync(d, { withFileTypes: true });
    } catch {
      return;
    }
    for (const e of entries) {
      const full = join(d, e.name);
      if (e.isDirectory()) walk(full);
      else if (e.name.endsWith('.dll')) {
        try {
          const st = statSync(full);
          seen.push({ file: full, mtimeMs: st.mtimeMs, size: st.size });
        } catch {
          /* vanished between readdir and stat: treated as a change on the next pass */
        }
      }
    }
  };
  for (const d of dirs) walk(d);
  seen.sort((a, b) => (a.file < b.file ? -1 : 1));
  return seen;
}

/** The first assembly that differs, described so the message can name it and both times. */
function firstChange(before, after) {
  const now = new Map(after.map((f) => [f.file, f]));
  for (const was of before) {
    const is = now.get(was.file);
    if (!is) return { file: was.file, was, is: null };
    if (is.mtimeMs !== was.mtimeMs || is.size !== was.size) return { file: was.file, was, is };
  }
  return null;
}

function reportSwap(change, startedAt) {
  const when = (ms) => new Date(ms).toISOString();
  console.error('');
  console.error('!! GATE RUN VOID — the assemblies changed while this run was executing them.');
  console.error(`!!   file:            ${change.file}`);
  console.error(`!!   run started:     ${startedAt.toISOString()}`);
  console.error(`!!   assembly was:    ${when(change.was.mtimeMs)} (${change.was.size} bytes)`);
  console.error(
    change.is
      ? `!!   assembly now:    ${when(change.is.mtimeMs)} (${change.is.size} bytes)`
      : '!!   assembly now:    deleted',
  );
  console.error('!! Whatever this run reports describes a mixture of builds and proves nothing in');
  console.error('!! either direction. Re-run it from a tree nobody else builds in.');
  console.error('');
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
    const owner = readOwner(dir);
    const legacy = legacyBlocker();
    if (!owner && !legacy) {
      console.log(`free (${lockFile(dir)})`);
    }
    if (owner) {
      const gone = !pidAlive(owner.pid);
      let note = gone ? '  [STALE — holder is gone]' : '';
      if (!gone && has('--probe')) {
        const p = await holderProgress(owner.pid);
        if (p.known) {
          note = p.cpuSeconds < 0.05
            ? `  [WEDGED? — no CPU in ${p.windowMs / 1000}s; ${p.total.toFixed(0)}s used in total]`
            : `  [working — ${p.cpuSeconds.toFixed(1)}s CPU in ${p.windowMs / 1000}s]`;
        }
      }
      console.log(
        `held by pid ${owner.pid} (${owner.label || 'unlabelled'}) on ${owner.host} since ${owner.since}`
          + `${owner.cwd ? ` in ${owner.cwd}` : ''}${note}`,
      );
    }
    if (legacy) {
      console.log(`held ${describeBlocker(legacy)}`);
    }
    const queue = liveTickets(dir);
    for (const [i, t] of queue.entries()) {
      console.log(`  waiting ${i + 1}: pid ${t.pid} (${t.label || 'unlabelled'}) since ${t.since}`);
    }
    return;
  }

  if (cmd === 'acquire') {
    const label = take('--label');
    if (has('--no-wait')) {
      if (!tryAcquire(dir, label)) {
        const b = blocker(dir);
        console.error(b ? `held by ${describeBlocker(b)}` : 'held');
        process.exit(3);
      }
    } else {
      await acquireWaiting(dir, label);
    }
    console.log('acquired');
    return;
  }

  if (cmd === 'steal') {
    const pid = Number(take('--pid'));
    const o = readOwner(dir);
    if (!o) {
      console.log('free');
      return;
    }
    if (!Number.isFinite(pid) || pid !== o.pid) {
      console.error(
        `refusing: name the holder you checked. The lock is held by pid ${o.pid}`
          + ` (${o.label || 'unlabelled'}) since ${o.since}.`,
      );
      process.exit(3);
    }
    const p = await holderProgress(o.pid, { windowMs: Math.max(ProbeWindowMs, 10_000) });
    if (p.known && p.cpuSeconds >= 0.05 && !has('--force')) {
      console.error(
        `refusing: pid ${o.pid} is working (${p.cpuSeconds.toFixed(1)}s CPU in`
          + ` ${p.windowMs / 1000}s). Use --force only if you mean to discard its run.`,
      );
      process.exit(3);
    }
    rmSync(lockFile(dir), { force: true });
    console.log(
      `stolen from pid ${o.pid} (${o.label || 'unlabelled'}); its process is left alone`,
    );
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
    const full = has('--full');
    const sep = args.indexOf('--');
    if (sep === -1 || sep === args.length - 1) {
      console.error('run needs a command after --');
      process.exit(1);
    }
    const command = args.slice(sep + 1);

    if (isUnfilteredApiSuite(command) && !full) {
      console.error(
        'gate-lock: refusing to start the whole API integration suite without --full.\n'
          + '  This command names the Api.Tests project and carries no --filter, so it is the full\n'
          + '  suite — hours of the machine-wide lock. If that is what you mean, say so:\n'
          + '    gate-lock.mjs run --full ... -- ' + command.join(' ') + '\n'
          + '  For a targeted tier, add the --filter that scripts/gate-affected.mjs printed.',
      );
      process.exit(3);
    }

    await acquireWaiting(dir, label);
    const startedAt = new Date();

    // Fingerprint after the lock, before the first test: anything built while we queued is fine,
    // anything built after this line is somebody overwriting the run in progress.
    const watchedDirs = outputDirsFor(command, process.cwd());
    const baseline = fingerprint(watchedDirs);
    let swap = null;

    // The child's output is teed rather than inherited, so this can read the runner's own summary
    // line on the way past. Nothing about what the caller sees changes.
    const child = spawn(command[0], command.slice(1), { stdio: ['inherit', 'pipe', 'pipe'] });
    let tail = '';
    const watch = (stream, out) =>
      stream?.on('data', (chunk) => {
        out.write(chunk);
        // Only the tail is kept: a full API suite prints tens of megabytes and the summary is at
        // the end, so buffering all of it would cost memory for nothing.
        tail = (tail + chunk.toString()).slice(-OutputTailBytes);
      });
    watch(child.stdout, process.stdout);
    watch(child.stderr, process.stderr);

    const forward = (sig) => child.kill(sig);
    process.on('SIGINT', forward);
    process.on('SIGTERM', forward);

    // Checked while the run is in flight rather than only at the end, because the point is to stop
    // burning the machine's long pole on an answer that cannot be used.
    const sentry =
      baseline.length > 0
        ? setInterval(() => {
            const change = firstChange(baseline, fingerprint(watchedDirs));
            if (!change) return;
            swap = change;
            reportSwap(change, startedAt);
            clearInterval(sentry);
            child.kill('SIGTERM');
          }, AssemblyCheckMs)
        : null;

    const exitCode = await new Promise((resolve) => {
      child.on('close', (code, signal) => resolve(code ?? (signal ? 128 : 1)));
      child.on('error', (e) => {
        console.error(e.message);
        resolve(127);
      });
    });

    if (sentry) clearInterval(sentry);
    const endedAt = new Date();
    // A swap in the last few seconds would otherwise slip past the interval and be reported green.
    if (!swap && baseline.length > 0) {
      const change = firstChange(baseline, fingerprint(watchedDirs));
      if (change) {
        swap = change;
        reportSwap(change, startedAt);
      }
    }
    if (resultFile) {
      const counts = summarise(tail);
      writeFileSync(
        resultFile,
        JSON.stringify(
          {
            label: label || '',
            command: command.join(' '),
            cwd: process.cwd(),
            full,
            lock: lockFile(dir),
            exitCode,
            ...counts,
            startedAt: startedAt.toISOString(),
            endedAt: endedAt.toISOString(),
            durationSeconds: Math.round((endedAt - startedAt) / 1000),
            host: hostname(),
            // Present and true only when the assemblies changed under the run. A reader who sees
            // this must discard every other field: they describe a mixture of builds.
            ...(swap
              ? {
                  verdict: 'void',
                  verdictReason: `assemblies changed under the run (${swap.file})`,
                  assemblySwap: swap,
                }
              : {}),
          },
          null,
          2,
        ),
      );
    }
    release(dir);
    // A void run must not exit 0: a caller that only checks the status code would otherwise read a
    // mixture of builds as a pass.
    process.exit(swap ? 75 : exitCode);
  }

  console.error('usage: gate-lock.mjs status [--probe] | acquire [--no-wait] [--label X] | release | steal --pid N [--force] | run [--label X] [--result F] [--full] -- cmd...');
  process.exit(1);
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  main().catch((e) => {
    console.error(e.message);
    process.exit(1);
  });
}
