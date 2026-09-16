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
// A `run` also fingerprints the test assemblies it is about to execute and re-checks them while it
// goes. If another worktree's build overwrites them mid-run the run is killed, the result file says
// `verdict: "void"`, and it exits 75 — because a verdict assembled from two builds proves nothing in
// either direction, and the failure is otherwise silent for as long as the suite takes.
//
// `run` is the normal form: take the lock (waiting in line by default), run the command with
// inherited stdio, write a small JSON result file when it ends (so a detached caller can
// collect the verdict later), release, and exit with the command's exit code. `acquire` with
// --no-wait exits 3 when the lock is held, so scripts can branch without parsing output.
//
// The lock directory defaults to the system temp dir and can be pointed elsewhere with
// SILEXGIS_GATE_LOCK_DIR — it must name the same place for every worktree that shares the
// machine, which the default already does.

import { mkdirSync, rmSync, readFileSync, readdirSync, writeFileSync, existsSync, statSync } from 'node:fs';
import { spawn } from 'node:child_process';
import { hostname, tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import process from 'node:process';

/** How much of a run's output is kept to find the summary in. */
/**
 * How often a run re-checks that nobody has rebuilt the assemblies it is executing. Overridable
 * only so the guard's own test can drive it; a minute is right for a suite measured in hours.
 */
const AssemblyCheckMs = Number(process.env.GATE_LOCK_ASSEMBLY_CHECK_MS || 60_000);
const OutputTailBytes = 64 * 1024;

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
export async function holderProgress(pid, { windowMs = 4000 } = {}) {
  const before = cpuSecondsOfTree(pid);
  if (before === null) return { known: false };
  await new Promise((r) => setTimeout(r, windowMs));
  const after = cpuSecondsOfTree(pid);
  if (after === null) return { known: false };
  return { known: true, cpuSeconds: after - before, windowMs, total: after };
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
      if (!o) {
        console.error('waiting for the gate lock');
      } else {
        // Whether the holder is working decides whether waiting is worth anything, so say which
        // it is rather than repeating the same line for hours against a process doing nothing.
        const p = await holderProgress(o.pid);
        const how = !p.known
          ? ''
          : p.cpuSeconds < 0.05
            ? ' — HOLDER IS BURNING NO CPU; if it is wedged, clear it with'
              + ` \`gate-lock.mjs steal --pid ${o.pid}\` after checking`
            : ` — holder is working (${p.cpuSeconds.toFixed(1)}s CPU in ${p.windowMs / 1000}s)`;
        console.error(
          `waiting for the gate lock, held by pid ${o.pid} (${o.label || 'unlabelled'})`
            + ` since ${o.since}${how}`,
        );
      }
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
      const gone = !pidAlive(o.pid);
      let note = gone ? '  [STALE — holder is gone]' : '';
      if (!gone && has('--probe')) {
        const p = await holderProgress(o.pid);
        if (p.known) {
          note = p.cpuSeconds < 0.05
            ? `  [WEDGED — no CPU in ${p.windowMs / 1000}s; ${p.total.toFixed(0)}s used in total]`
            : `  [working — ${p.cpuSeconds.toFixed(1)}s CPU in ${p.windowMs / 1000}s]`;
        }
      }
      console.log(
        `held by pid ${o.pid} (${o.label || 'unlabelled'}) on ${o.host} since ${o.since}` + note,
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

  if (cmd === 'steal') {
    const pid = Number(take('--pid'));
    const o = existsSync(dir) ? readOwner(dir) : null;
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
    const p = await holderProgress(o.pid, { windowMs: 10_000 });
    if (p.known && p.cpuSeconds >= 0.05 && !has('--force')) {
      console.error(
        `refusing: pid ${o.pid} is working (${p.cpuSeconds.toFixed(1)}s CPU in`
          + ` ${p.windowMs / 1000}s). Use --force only if you mean to discard its run.`,
      );
      process.exit(3);
    }
    rmSync(dir, { recursive: true, force: true });
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
  // `dotnet test <path>` is the shape every caller uses; the assemblies live under that project's
  // bin/. Anything else falls back to the working directory, which over-collects rather than
  // under-collects — a false alarm costs a re-run, a miss costs a day.
  const target = command.find((a) => a.endsWith('.csproj') || a.endsWith('.slnx') || a.endsWith('.sln'));
  const base = target ? dirname(resolve(cwd, target)) : cwd;
  const bin = join(base, 'bin');
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

  console.error('usage: gate-lock.mjs status [--probe] | acquire [--no-wait] [--label X] | release | steal --pid N [--force] | run [--label X] [--result F] -- cmd...');
  process.exit(1);
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  main().catch((e) => {
    console.error(e.message);
    process.exit(1);
  });
}
