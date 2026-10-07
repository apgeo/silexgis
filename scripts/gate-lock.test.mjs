// SPDX-License-Identifier: AGPL-3.0-or-later

// Tests for the gate lock: taking it, queueing behind it in arrival order, taking over a holder
// that died or that is alive and doing nothing, honouring a lock an older copy of the tool took
// under the temporary directory, refusing an unfiltered full suite, and the `run` form releasing
// the lock and recording a verdict whatever the command's outcome.
//
// Run from the repository root with `node --test "scripts/**/*.test.mjs"`. Each test points
// SILEXGIS_GATE_LOCK_DIR at its own temporary directory, and the whole file points the legacy
// location at an empty one, so nothing here can see or disturb a real integration run on the
// same machine.

import { strict as assert } from 'node:assert';
import { spawn, spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync, mkdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { after, describe, it } from 'node:test';

import {
  tryAcquire,
  release,
  readOwner,
  lockFile,
  liveTickets,
  legacyBlocker,
  isUnfilteredApiSuite,
  outputDirsFor,
  testModeOf,
  inMode,
  summarise,
  cpuSecondsOfTree,
  holderProgress,
} from './gate-lock.mjs';

const script = join(dirname(fileURLToPath(import.meta.url)), 'gate-lock.mjs');
const scratch = mkdtempSync(join(tmpdir(), 'gate-lock-test-'));
after(() => rmSync(scratch, { recursive: true, force: true }));

const freshDir = (name) => join(scratch, name);
// The machine's real legacy lock may be held by a real run while these tests execute; every call
// in this file, in-process or spawned, looks at an empty stand-in instead.
process.env.SILEXGIS_GATE_LEGACY_DIR = freshDir('no-legacy-lock');
const envFor = (dir, extra = {}) => ({ ...process.env, SILEXGIS_GATE_LOCK_DIR: dir, ...extra });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
/**
 * Waits for something to become true, and fails by name when it does not.
 *
 * The cases below start several processes and need them to have reached a known point — a ticket
 * taken, a lock held — before the next one starts. A fixed pause is right on an idle machine and
 * wrong on a busy one, where starting a process alone can outlast it; and this suite runs beside
 * whatever else the machine is doing.
 */
const until = async (what, check, timeoutMs = 30_000) => {
  const deadline = Date.now() + timeoutMs;
  while (!check()) {
    assert.ok(Date.now() < deadline, `timed out waiting for ${what}`);
    await sleep(50);
  }
};
const deadPid = () => spawnSync(process.execPath, ['-e', '']).pid;

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

  it('the lock is one named file, and its directory outlives a release', () => {
    const dir = freshDir('shape');
    assert.equal(tryAcquire(dir, 'held'), true);
    assert.ok(existsSync(lockFile(dir)), 'the claim is the file');
    const record = JSON.parse(readFileSync(lockFile(dir), 'utf8'));
    assert.equal(record.pid, process.pid);
    assert.ok(record.cwd, 'the record names the worktree that holds it');
    release(dir);
    assert.equal(existsSync(lockFile(dir)), false, 'released means the file is gone');
    assert.ok(existsSync(dir), 'the data directory is not the lock and is not removed');
  });

  it('a holder whose process is gone is stale and is taken over', () => {
    const dir = freshDir('stale');
    mkdirSync(dir, { recursive: true });
    writeFileSync(
      lockFile(dir),
      JSON.stringify({ pid: deadPid(), host: 'gone', label: 'crashed run', since: '2026-01-01T00:00:00Z' }),
    );
    assert.equal(tryAcquire(dir, 'successor'), true);
    assert.equal(readOwner(dir).label, 'successor');
    release(dir);
  });

  it('a release by somebody who does not hold the lock changes nothing', () => {
    const dir = freshDir('not-mine');
    assert.equal(tryAcquire(dir, 'mine'), true);
    assert.equal(release(dir, { heldByPid: 424242 }), false);
    assert.equal(readOwner(dir).label, 'mine');
    release(dir);
  });

  it('the command line refuses a held lock with exit 3 under --no-wait', () => {
    const dir = freshDir('cli');
    assert.equal(tryAcquire(dir, 'holder'), true);
    const r = spawnSync(process.execPath, [script, 'acquire', '--no-wait', '--label', 'late'], {
      env: envFor(dir),
      encoding: 'utf8',
    });
    assert.equal(r.status, 3);
    release(dir);
  });
});

describe('a lock an older copy of the tool took under the temporary directory', () => {
  // The changeover hazard: moving the lock while an older copy still holds the old one is two
  // mutually invisible locks, which is the failure that produced sixteen phantom failures over
  // three hours. So the old location is read and honoured until its holder is gone.
  it('blocks while its holder lives, and is cleared once the holder is gone', () => {
    const dir = freshDir('legacy');
    const legacy = freshDir('legacy-dir');
    const saved = process.env.SILEXGIS_GATE_LEGACY_DIR;
    process.env.SILEXGIS_GATE_LEGACY_DIR = legacy;
    try {
      mkdirSync(legacy);
      writeFileSync(join(legacy, 'owner.json'), JSON.stringify({ pid: process.pid, label: 'old tool', since: '2026-10-05T00:00:00Z' }));
      assert.equal(tryAcquire(dir, 'new tool'), false, 'a live legacy holder is a running suite');
      assert.equal(legacyBlocker().kind, 'legacy');
      assert.equal(existsSync(lockFile(dir)), false, 'nothing was written over it');

      writeFileSync(join(legacy, 'owner.json'), JSON.stringify({ pid: deadPid(), label: 'old tool', since: '2026-10-05T00:00:00Z' }));
      assert.equal(tryAcquire(dir, 'new tool'), true, 'a dead legacy holder is no claim');
      assert.equal(existsSync(legacy), false, 'and its corpse is cleared, because nothing else will');
      release(dir);
    } finally {
      process.env.SILEXGIS_GATE_LEGACY_DIR = saved;
    }
  });
});

describe('waiting in turn', () => {
  it('waiters take the lock in the order they arrived', async () => {
    const dir = freshDir('fifo');
    assert.equal(tryAcquire(dir, 'holder'), true);
    const env = envFor(dir);
    const order = [];
    const waiter = (label) => {
      const child = spawn(process.execPath, [script, 'acquire', '--label', label], { env });
      const exited = new Promise((r) => child.on('exit', r));
      child.stdout.on('data', (d) => {
        if (String(d).includes('acquired')) order.push(label);
      });
      return exited;
    };
    const queued = () => liveTickets(dir).map((t) => t.label);
    const a = waiter('A');
    await until('A to join the queue', () => queued().includes('A'));
    const b = waiter('B');
    await until('B to join the queue', () => queued().includes('B'));
    assert.deepEqual(queued(), ['A', 'B'], 'both are queued, A first');

    release(dir);
    // A takes the lock and exits holding it; its claim is then stale, which is B's cue.
    await a;
    await b;
    assert.deepEqual(order, ['A', 'B']);
    assert.deepEqual(liveTickets(dir), [], 'nobody is left in the queue');
    release(dir, { heldByPid: -1 });
  });
});

/**
 * A process that is doing nothing, handed over once it really is.
 *
 * Starting up costs a node process tens of milliseconds of CPU, and on a busy machine that start
 * can still be going on when a test begins to watch the process for idleness — which then reads as
 * an idle process burning CPU, about one run in four beside a full suite. So the child says when
 * it is up, and nobody looks at it before.
 */
function idleProcess() {
  const child = spawn(process.execPath, ['-e', "console.log('up'); setTimeout(() => {}, 60000);"], {
    stdio: ['ignore', 'pipe', 'ignore'],
  });
  return new Promise((resolve, reject) => {
    child.once('error', reject);
    child.stdout.once('data', () => resolve(child));
  });
}

describe('telling a wedged holder from a slow one', () => {
  // The lock already takes over from a holder that died. What it could not see is a holder still
  // running and doing nothing, which is what wedged this machine twice — eight and twelve hours
  // on the lock at around two per cent of a core, with other sessions queued behind it. Age does
  // not distinguish that from a full suite legitimately running for hours; CPU does.

  it('accounts for the CPU of a process and everything under it', () => {
    const own = cpuSecondsOfTree(process.pid);
    if (own === null) return; // no /proc: the caller treats this as "cannot tell"
    assert.ok(own >= 0, 'a running process has consumed some non-negative amount of CPU');
    assert.ok(Number.isFinite(own));
  });

  it('answers null for a pid that is not there, rather than zero', () => {
    if (cpuSecondsOfTree(process.pid) === null) return; // no /proc
    // Zero would read as "present and idle", which is the one conclusion that must not be
    // reached about a process that does not exist.
    assert.equal(cpuSecondsOfTree(0x7ffffff0), null);
  });

  it('sees a busy process as busy', async () => {
    const child = spawn(process.execPath, ['-e', 'const end = Date.now() + 5000; while (Date.now() < end);']);
    try {
      const p = await holderProgress(child.pid, { windowMs: 1500 });
      if (!p.known) return; // no /proc
      assert.ok(p.cpuSeconds > 0.2, `a spinning process should burn CPU, saw ${p.cpuSeconds}`);
    } finally {
      child.kill('SIGKILL');
    }
  });

  it('sees an idle process as idle', async () => {
    const child = await idleProcess();
    try {
      const p = await holderProgress(child.pid, { windowMs: 1500 });
      if (!p.known) return; // no /proc
      assert.ok(p.cpuSeconds < 0.05, `a sleeping process should burn none, saw ${p.cpuSeconds}`);
    } finally {
      child.kill('SIGKILL');
    }
  });

  it('refuses to steal from a holder whose pid the caller did not name', () => {
    const dir = freshDir('steal-unnamed');
    assert.ok(tryAcquire(dir, 'holder'));
    const r = spawnSync(process.execPath, [script, 'steal', '--pid', '424242'], {
      env: envFor(dir),
      encoding: 'utf8',
    });
    assert.equal(r.status, 3, 'naming the wrong holder must not release anybody else\'s lock');
    assert.ok(existsSync(lockFile(dir)), 'the lock is still held');
    release(dir);
  });

  it('steals from a holder that is doing nothing, and leaves its process alone', async () => {
    const dir = freshDir('steal-wedged');
    const child = await idleProcess();
    try {
      assert.ok(tryAcquire(dir, 'wedged'));
      // Rewrite the owner so the recorded holder is the idle child rather than this test.
      const owner = readOwner(dir);
      writeFileSync(lockFile(dir), JSON.stringify({ ...owner, pid: child.pid }));
      const r = spawnSync(process.execPath, [script, 'steal', '--pid', String(child.pid)], {
        env: envFor(dir, { GATE_LOCK_PROBE_MS: '1500' }),
        encoding: 'utf8',
      });
      if (cpuSecondsOfTree(process.pid) === null) return; // no /proc: steal cannot judge
      assert.equal(r.status, 0, r.stderr);
      assert.ok(!existsSync(lockFile(dir)), 'the lock is free');
      assert.equal(child.killed, false, 'stealing the lock does not kill the holder');
    } finally {
      child.kill('SIGKILL');
    }
  });

  it('a waiter takes over from a holder that burns no CPU for the whole window, by itself', async () => {
    const dir = freshDir('auto-takeover');
    const sleeper = await idleProcess();
    try {
      mkdirSync(dir, { recursive: true });
      writeFileSync(
        lockFile(dir),
        JSON.stringify({ pid: sleeper.pid, host: 'here', label: 'wedged suite', since: new Date().toISOString() }),
      );
      const r = spawnSync(process.execPath, [script, 'acquire', '--label', 'late'], {
        env: envFor(dir, { GATE_LOCK_WEDGED_MS: '2000', GATE_LOCK_PROBE_MS: '300' }),
        encoding: 'utf8',
        timeout: 40_000,
      });
      if (cpuSecondsOfTree(process.pid) === null) return; // no /proc: nothing can be judged wedged
      assert.equal(r.status, 0, r.stderr);
      assert.match(r.stderr, /WEDGED/, 'the takeover is said out loud');
      assert.equal(readOwner(dir).label, 'late');
      assert.equal(sleeper.killed, false, 'the wedged process is left alone');
    } finally {
      sleeper.kill('SIGKILL');
      release(dir, { heldByPid: -1 });
    }
  });
});

describe('a result says which test mode produced it', () => {
  const sharded = ['node', 'scripts/gate-sharded.mjs', '--count', '8'];
  const plain = ['dotnet', 'test', 'x.csproj', '--filter', 'A'];

  it('reads the mode from whichever of the three places says it, and is precise when none does', () => {
    assert.equal(testModeOf(sharded, {}), 'precise');
    assert.equal(testModeOf([...sharded, '--mode', 'fast'], {}), 'fast');
    assert.equal(testModeOf([...sharded, '--mode=fast'], {}), 'fast');
    assert.equal(testModeOf(sharded, { SILEXGIS_TEST_MODE: 'Fast' }), 'fast');
    assert.equal(testModeOf(plain, { SILEXGIS_TEST_MODE: 'fast' }), 'fast');
    assert.equal(testModeOf(plain, {}, 'fast'), 'fast');
    // Said twice and alike is said once.
    assert.equal(testModeOf([...sharded, '--mode', 'fast'], { SILEXGIS_TEST_MODE: 'fast' }, 'fast'), 'fast');
  });

  it('refuses a mode said two ways, and a mode there is not', () => {
    assert.throws(() => testModeOf([...sharded, '--mode', 'precise'], { SILEXGIS_TEST_MODE: 'fast' }), /two ways/);
    assert.throws(() => testModeOf(plain, { SILEXGIS_TEST_MODE: 'fast' }, 'precise'), /two ways/);
    assert.throws(() => testModeOf(plain, { SILEXGIS_TEST_MODE: 'quick' }), /fast or precise/);
    assert.throws(() => testModeOf(plain, {}, 'faster'), /fast or precise/);
  });

  it("leaves another tool's --mode alone", () => {
    // Only the sharded runner's option is the test mode; anybody else's is theirs.
    assert.equal(testModeOf(['npx', 'vite', 'build', '--mode', 'development'], {}), 'precise');
  });

  it('never calls a fast pass green', () => {
    const passed = summarise('Passed!  - Failed:     0, Passed:    33, Skipped:     0, Total:    33, Duration: 1 s - X.dll');
    assert.equal(passed.verdict, 'green');
    assert.equal(inMode(passed, 'precise').verdict, 'green');
    assert.equal(inMode(passed, 'fast').verdict, 'green-fast');
    assert.match(inMode(passed, 'fast').verdictReason, /precise run/);

    const failed = summarise('Failed!  - Failed:     2, Passed:    31, Skipped:     0, Total:    33, Duration: 1 s - X.dll');
    assert.equal(inMode(failed, 'fast').verdict, 'red');
    assert.match(inMode(failed, 'fast').verdictReason, /2 of 33 failed, in fast mode/);

    // Nothing ran is nothing ran, in either mode.
    assert.equal(inMode(summarise(''), 'fast').verdict, 'inconclusive');
  });

  const runIn = (name, options, environment, child) => {
    const dir = freshDir(name);
    const result = join(scratch, `${name}.json`);
    const r = spawnSync(
      process.execPath,
      [script, 'run', '--result', result, ...options, '--', process.execPath, '-e', child],
      // Whatever mode the shell running these tests happens to be in is not part of the question.
      { env: envFor(dir, { SILEXGIS_TEST_MODE: '', ...environment }), encoding: 'utf8' },
    );
    return { r, result };
  };
  const summaryOfAPass = "console.log('Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 1 s - X.dll')";

  it('writes the mode into the result file', () => {
    const { r, result } = runIn('mode-env', [], { SILEXGIS_TEST_MODE: 'fast' }, summaryOfAPass);
    assert.equal(r.status, 0, r.stderr);
    const written = JSON.parse(readFileSync(result, 'utf8'));
    assert.equal(written.testMode, 'fast');
    assert.equal(written.verdict, 'green-fast');
  });

  it('hands the mode it was asked for to the command, and records a precise pass as green', () => {
    const tellsItsMode = `console.log('mode=' + process.env.SILEXGIS_TEST_MODE); ${summaryOfAPass}`;
    const fast = runIn('mode-fast', ['--mode', 'fast'], {}, tellsItsMode);
    assert.equal(fast.r.status, 0, fast.r.stderr);
    assert.match(fast.r.stdout, /mode=fast/);
    assert.match(fast.r.stderr, /test mode fast/);
    assert.equal(JSON.parse(readFileSync(fast.result, 'utf8')).verdict, 'green-fast');

    const precise = runIn('mode-precise', [], {}, tellsItsMode);
    assert.equal(precise.r.status, 0, precise.r.stderr);
    assert.match(precise.r.stdout, /mode=precise/);
    assert.equal(JSON.parse(readFileSync(precise.result, 'utf8')).verdict, 'green');
    assert.equal(JSON.parse(readFileSync(precise.result, 'utf8')).testMode, 'precise');
  });

  it('starts nothing when the mode is said two ways', () => {
    const { r, result } = runIn('mode-twice', ['--mode', 'precise'], { SILEXGIS_TEST_MODE: 'fast' }, summaryOfAPass);
    assert.equal(r.status, 1);
    assert.match(r.stderr, /two ways/);
    assert.equal(existsSync(result), false);
  });

  it("does not take the wrapped command's --mode for its own", () => {
    // After the separator it is the command's: handed on untouched, and not the test mode.
    const dir = freshDir('mode-theirs');
    const result = join(scratch, 'mode-theirs.json');
    const r = spawnSync(
      process.execPath,
      [
        script, 'run', '--result', result, '--',
        process.execPath, '-e', `console.log(process.argv.slice(1).join(' ')); ${summaryOfAPass}`,
        // The second separator is node's own: what follows it is the script's, not node's.
        '--', '--mode', 'development',
      ],
      { env: envFor(dir, { SILEXGIS_TEST_MODE: '' }), encoding: 'utf8' },
    );
    assert.equal(r.status, 0, r.stderr);
    assert.match(r.stdout, /--mode development/);
    assert.equal(JSON.parse(readFileSync(result, 'utf8')).testMode, 'precise');
  });

  it('wants a mode after --mode', () => {
    const { r } = runIn('mode-missing', ['--mode'], {}, summaryOfAPass);
    assert.equal(r.status, 1);
    assert.match(r.stderr, /fast or precise/);
  });
});

describe('the whole API suite is a deliberate act', () => {
  it('knows the suite by its project, by its directory, and by the absence of a filter', () => {
    assert.equal(isUnfilteredApiSuite(['dotnet', 'test', 'tests/SilexGis.Api.Tests', '--no-build']), true);
    assert.equal(isUnfilteredApiSuite(['dotnet', 'test', 'tests/SilexGis.Api.Tests/']), true);
    assert.equal(
      isUnfilteredApiSuite(['C:\\Program Files\\dotnet\\dotnet.exe', 'test', 'C:\\x\\tests\\SilexGis.Api.Tests\\SilexGis.Api.Tests.csproj']),
      true,
    );
    assert.equal(isUnfilteredApiSuite(['dotnet', 'test', 'tests/SilexGis.Api.Tests', '--filter', 'FullyQualifiedName~X']), false);
    assert.equal(isUnfilteredApiSuite(['dotnet', 'test', 'tests/SilexGis.Api.Tests', '--list-tests']), false);
    assert.equal(isUnfilteredApiSuite(['dotnet', 'test', 'tests/SilexGis.Domain.Tests']), false);
    assert.equal(isUnfilteredApiSuite(['dotnet', 'build', 'tests/SilexGis.Api.Tests']), false);
    assert.equal(isUnfilteredApiSuite([process.execPath, '-e', '1']), false);
  });

  it('knows the suite by its other door, the runner that deals it into shards', () => {
    assert.equal(isUnfilteredApiSuite(['node', 'scripts/gate-sharded.mjs', '--count', '8']), true);
    assert.equal(
      isUnfilteredApiSuite([process.execPath, 'C:\\repo\\scripts\\gate-sharded.mjs', '--project', 'x.csproj']),
      true,
    );
    // Some shards of the deal are not the suite, as a filter is not; nor are some of its classes.
    assert.equal(isUnfilteredApiSuite(['node', 'scripts/gate-sharded.mjs', '--only', '3']), false);
    assert.equal(isUnfilteredApiSuite(['node', 'scripts/gate-sharded.mjs', '--classes', 'CalendarTests']), false);
    assert.equal(isUnfilteredApiSuite(['node', 'scripts/gate-sharded.mjs', '--affected', 'master']), false);
    assert.equal(isUnfilteredApiSuite(['node', 'scripts/gate-shard.mjs', '--index', '0', '--count', '8']), false);
  });

  it('guards the assemblies the sharded runner tests, whether or not it was told which', () => {
    const repo = freshDir('sharded-guard');
    const bin = join(repo, 'server', 'tests', 'SilexGis.Api.Tests', 'bin');
    mkdirSync(bin, { recursive: true });
    mkdirSync(join(repo, 'scripts'), { recursive: true });
    const elsewhere = join(freshDir('sharded-elsewhere'), 'tests', 'SilexGis.Api.Tests');
    mkdirSync(join(elsewhere, 'bin'), { recursive: true });
    writeFileSync(join(elsewhere, 'SilexGis.Api.Tests.csproj'), '<Project />');

    // Told nothing, it tests the project beside itself.
    assert.deepEqual(outputDirsFor(['node', join(repo, 'scripts', 'gate-sharded.mjs')], scratch), [bin]);
    // Told a project in another checkout, that is the one whose assemblies must not change.
    assert.deepEqual(
      outputDirsFor(
        ['node', join(repo, 'scripts', 'gate-sharded.mjs'), '--project', join(elsewhere, 'SilexGis.Api.Tests.csproj')],
        scratch,
      ),
      [join(elsewhere, 'bin')],
    );
  });

  it('refuses the whole suite without --full, before taking the lock or running anything', () => {
    const dir = freshDir('refuse');
    const result = join(scratch, 'refused.json');
    const r = spawnSync(
      process.execPath,
      [script, 'run', '--result', result, '--', 'dotnet', 'test', 'server/tests/SilexGis.Api.Tests/SilexGis.Api.Tests.csproj', '--no-build'],
      { env: envFor(dir), encoding: 'utf8' },
    );
    assert.equal(r.status, 3);
    assert.match(r.stderr, /--full/, 'it says what to type when the full suite is meant');
    assert.equal(existsSync(lockFile(dir)), false, 'no lock was taken');
    assert.equal(existsSync(result), false, 'no verdict was written');
  });
});

describe('run', () => {
  it('runs the command, records the verdict, propagates the exit code, and releases', () => {
    const dir = freshDir('run');
    const result = join(scratch, 'result.json');
    const r = spawnSync(
      process.execPath,
      [script, 'run', '--label', 'probe', '--result', result, '--', process.execPath, '-e', 'process.exit(3)'],
      { env: envFor(dir), encoding: 'utf8' },
    );
    assert.equal(r.status, 3);
    const verdict = JSON.parse(readFileSync(result, 'utf8'));
    assert.equal(verdict.exitCode, 3);
    assert.equal(verdict.label, 'probe');
    assert.equal(verdict.lock, lockFile(dir), 'the verdict names the lock it ran under');
    assert.ok(verdict.startedAt <= verdict.endedAt);
    assert.equal(existsSync(lockFile(dir)), false, 'the lock must be released after the run');
  });

  it('a green command reports exit 0 the same way', () => {
    const dir = freshDir('run-green');
    const result = join(scratch, 'result-green.json');
    const r = spawnSync(
      process.execPath,
      [script, 'run', '--result', result, '--', process.execPath, '-e', 'process.exit(0)'],
      { env: envFor(dir), encoding: 'utf8' },
    );
    assert.equal(r.status, 0);
    assert.equal(JSON.parse(readFileSync(result, 'utf8')).exitCode, 0);
    assert.equal(existsSync(lockFile(dir)), false);
  });

  it('a second run waits for the first and both record verdicts', async () => {
    const dir = freshDir('run-queue');
    const r1 = join(scratch, 'q1.json');
    const r2 = join(scratch, 'q2.json');
    const env = envFor(dir);
    const first = spawn(process.execPath, [script, 'run', '--result', r1, '--', process.execPath, '-e', 'setTimeout(() => {}, 2500)'], { env });
    await until('the first run to hold the lock', () => readOwner(dir)?.pid === first.pid);
    const second = spawn(process.execPath, [script, 'run', '--result', r2, '--', process.execPath, '-e', 'process.exit(0)'], { env });
    await new Promise((r) => first.on('exit', r));
    await new Promise((r) => second.on('exit', r));
    const v1 = JSON.parse(readFileSync(r1, 'utf8'));
    const v2 = JSON.parse(readFileSync(r2, 'utf8'));
    assert.ok(v2.startedAt >= v1.endedAt, 'the second started only once the first had finished');
    assert.equal(existsSync(lockFile(dir)), false);
  });
});

describe('a result file says what actually ran', () => {
  const runWith = (name, output) => {
    const dir = freshDir(`verdict-${name}`);
    const result = join(scratch, `result-${name}.json`);
    spawnSync(
      process.execPath,
      [script, 'run', '--result', result, '--', process.execPath, '-e', `console.log(${JSON.stringify(output)})`],
      { env: envFor(dir), encoding: 'utf8' },
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

// Two full runs were destroyed by this and neither reported anything wrong: another worktree's
// build rewrote the `.dll` files a run was executing, hours in, and the run carried on and produced
// a verdict describing a mixture of two builds. It was only ever visible afterwards, by noticing
// the test *total* disagreed with neighbouring runs. So the guard has to be the thing that notices.
describe('assemblies swapped under a run', () => {
  const project = (name) => {
    const root = join(scratch, name);
    const bin = join(root, 'bin', 'Debug', 'net10.0');
    mkdirSync(bin, { recursive: true });
    writeFileSync(join(root, `${name}.csproj`), '<Project />');
    writeFileSync(join(bin, 'Suite.dll'), 'build one');
    return { root, csproj: join(root, `${name}.csproj`), dll: join(bin, 'Suite.dll') };
  };

  it('is caught, killed, and reported void rather than passed off as a verdict', () => {
    const dir = freshDir('swap');
    const result = join(scratch, 'result-swap.json');
    const p = project('Swapped');
    // The command outlives the first check, so the rewrite lands mid-run exactly as a sibling
    // worktree's build does.
    const r = spawnSync(
      process.execPath,
      [script, 'run', '--label', 'swap', '--result', result, '--',
        process.execPath, '-e', `setTimeout(() => {}, 4000); require('fs').writeFileSync(${JSON.stringify(p.dll)}, 'build two')`],
      {
        env: envFor(dir, { GATE_LOCK_ASSEMBLY_CHECK_MS: '150' }),
        cwd: p.root,
        encoding: 'utf8',
      },
    );

    const verdict = JSON.parse(readFileSync(result, 'utf8'));
    assert.equal(verdict.verdict, 'void', 'a run whose assemblies changed is not a verdict');
    assert.match(verdict.verdictReason, /assemblies changed/);
    assert.equal(verdict.assemblySwap.file, p.dll);
    assert.notEqual(r.status, 0, 'a void run must not exit 0, or a caller checking only the status reads a pass');
    assert.match(r.stderr, /GATE RUN VOID/, 'it must say so where somebody watching would see it');
    assert.match(r.stderr, new RegExp('Suite\\.dll'), 'the message must name the file');
    assert.equal(existsSync(lockFile(dir)), false, 'the lock is still released');
  });

  // The project named by its DIRECTORY, from somewhere else, is the shape every batch brief uses.
  // It used to fall through to the working directory's bin/, which does not exist, so those runs
  // had no guard at all and nothing said so.
  it('is caught when the project is named by its directory from another working directory', () => {
    const dir = freshDir('swap-dir');
    const result = join(scratch, 'result-swap-dir.json');
    const p = project('SwappedByDir');
    const r = spawnSync(
      process.execPath,
      [script, 'run', '--result', result, '--',
        process.execPath, '-e', `setTimeout(() => {}, 4000); require('fs').writeFileSync(${JSON.stringify(p.dll)}, 'build two')`,
        p.root],
      {
        env: envFor(dir, { GATE_LOCK_ASSEMBLY_CHECK_MS: '150' }),
        cwd: scratch,
        encoding: 'utf8',
      },
    );
    assert.equal(JSON.parse(readFileSync(result, 'utf8')).verdict, 'void');
    assert.equal(r.status, 75);
  });

  // The twin: without this, a guard that flagged every run would pass the test above and make the
  // gate useless.
  it('an untouched run is not flagged, and says which tree it described', () => {
    const dir = freshDir('no-swap');
    const result = join(scratch, 'result-no-swap.json');
    const p = project('Untouched');
    const r = spawnSync(
      process.execPath,
      [script, 'run', '--result', result, '--', process.execPath, '-e', 'setTimeout(() => {}, 500)'],
      {
        env: envFor(dir, { GATE_LOCK_ASSEMBLY_CHECK_MS: '100' }),
        cwd: p.root,
        encoding: 'utf8',
      },
    );
    assert.equal(r.status, 0);
    const verdict = JSON.parse(readFileSync(result, 'utf8'));
    // `verdict` already carries red/green/inconclusive from the runner's own summary; `void` joins
    // that vocabulary rather than adding a second field, so the check is that it is NOT void.
    assert.notEqual(verdict.verdict, 'void');
    assert.equal(verdict.assemblySwap, undefined);
    // "Which checkout did this verdict describe" could not be answered about any run of 2026-09-16
    // without reading /proc.
    assert.equal(verdict.cwd, p.root);
  });
});
