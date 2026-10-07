// SPDX-License-Identifier: AGPL-3.0-or-later

// The whole API integration suite on one machine, dealt into shards that run side by side.
//
// A full run started the ordinary way is one test process against one PostGIS container, and a
// test process does not use the machine it is given. Measured over this suite: the tests' own
// time is about a tenth of a run, the rest goes between them — chiefly to building a host for
// each test — and a process doing that used about two cores with four test threads to spend.
// Threads do not spread that work; processes do. So this starts several, each given a share of
// the classes by the same deal the integration workflow uses for its hosted runners
// (scripts/gate-shard.mjs — every class in exactly one shard) and each standing up a database
// server of its own. Eight of them ran 2,614 tests in fifty-four minutes on a machine where the
// single process had been taking a day beside other work.
//
// One kind of class is kept out of the deal: a member of the suite's serial collection asserts
// that something stays responsive, which measures the machine as much as the code, and the suite
// runs such a class with nothing beside it in its process. Beside seven other processes that
// would mean nothing, so those classes run last and alone, once every shard has ended.
//
// It builds nothing. The assemblies it runs are the ones already built for the project, so that
// what was built and what was tested are the same thing; build first.
//
// Usage, from the repository root:
//   node scripts/gate-sharded.mjs [--count 8] [--threads 4] [--results <dir>] [--project <csproj>]
//                                 [--only 2,5,quiet] [--mode precise|fast]
//                                 [--classes A,B,C | --affected <base>]
//
// --classes deals only the named classes, for a run that is not the whole suite, and --affected
// deals the classes that answer for the working tree's changes against <base>, as
// scripts/gate-affected.mjs selects them: they cost the same per test as any others, and several
// processes pay it side by side just as well for forty classes as for all of them. Fewer
// processes are started for fewer classes, since each stands up a database server before its
// first test; --count is then the most there may be. A change that selects the whole suite is
// not run under this name: the runner says so and exits 10, as gate-affected does.
//
// --mode says how much of its surroundings each test has to itself. `precise`, the default, is the
// suite as written: an application built for every test. `fast` lets the tests of a class take
// turns on one running application where they ask for the same one, which is several times
// quicker and proves less — see TestMode in the test project's Support directory. The mode is
// handed to the test processes as SILEXGIS_TEST_MODE, printed first and last in the output, and
// checked afterwards against what the processes say they did: how many applications they built,
// and how many times a test was handed one already running.
//
// --only runs just those shards of the deal (and `quiet`, the classes that run alone), for the
// one that died of something other than its tests. The classes are read from beside the project
// that is run, so a project in another checkout is dealt by that checkout's classes.
//
// Each run writes `shard-<n>.trx` and `shard-<n>.log` (`quiet.trx`, `quiet.log`) into the results
// directory. When the last one ends, the runner's own summary line of every one is printed again,
// one per line, so that whatever reads this output for a verdict — scripts/gate-lock.mjs does —
// adds up the same lines it would have read from a single run. The exit code is 0 only when every
// one's was.

import { spawn } from 'node:child_process';
import { createWriteStream, existsSync, mkdirSync, readFileSync, rmSync } from 'node:fs';
import { availableParallelism } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import process from 'node:process';

import { filterExpression, selectionFor } from './gate-affected.mjs';
import { listClasses, shard, testDir } from './gate-shard.mjs';

/**
 * The classes that must run with nothing beside them: the members of the serial collection.
 *
 * Read from the sources, where the membership is declared, so that a class joining the collection
 * tomorrow is kept out of the deal without anybody remembering to say so here.
 */
export function quietClasses(dir, classes) {
  return classes.filter((name) =>
    /\[Collection\(SerialCollection\.Name\)\]/.test(readFileSync(join(dir, `${name}.cs`), 'utf8')));
}

/** The runner's last summary line in a shard's output, or null when it never printed one. */
export function summaryLineOf(output) {
  const lines = (output || '').match(
    /^\s*(?:Passed|Failed|Skipped)!\s*-\s*Failed:\s*\d+,\s*Passed:\s*\d+,\s*Skipped:\s*\d+,\s*Total:\s*\d+.*$/gm,
  );
  return lines ? lines.at(-1).trim() : null;
}

/**
 * How many test threads each shard gets when the caller does not say.
 *
 * The machine's cores shared out, and never fewer than two: a class waits on its database far
 * more than it computes, so a shard with one thread spends most of its time idle.
 */
export function threadsFor(count, cores = availableParallelism()) {
  // And never more than eight, the suite's own setting: past that a single process was measured
  // gaining nothing, so a lone shard is not handed the whole machine to idle on.
  return Math.min(8, Math.max(2, Math.floor(cores / count)));
}

/**
 * How many shards a run of some classes is dealt into: `count` at most, and no more than the
 * classes are worth.
 *
 * Every shard starts a database server and builds the schema into it before its first test —
 * a quarter of a minute nobody gets back. In precise mode a class is minutes of application
 * building, so a process for every two classes still pays; in fast mode a class is seconds, and
 * it takes half a dozen to be worth a server. The whole suite is dealt as asked.
 */
export function sharesFor(count, classes, { restricted, mode }) {
  if (!restricted) return count;
  return Math.max(1, Math.min(count, Math.ceil(classes / (mode === 'fast' ? 6 : 2))));
}

/** Reads `--name value` options; anything it does not know is an error rather than a guess. */
export function parseArguments(argv) {
  const options = {
    count: 8, threads: null, results: null, project: null, only: null, mode: null, classes: null,
    affected: null,
  };
  const known = [
    '--count', '--threads', '--results', '--project', '--only', '--mode', '--classes', '--affected',
  ];
  for (let index = 0; index < argv.length; index += 1) {
    const name = argv[index];
    const value = argv[index + 1];
    if (!known.includes(name) || value === undefined) {
      throw new Error(`unknown or incomplete argument: ${name}`);
    }
    index += 1;
    if (name === '--count' || name === '--threads') {
      const number = Number(value);
      if (!Number.isInteger(number) || number < 1) {
        throw new Error(`${name} takes a whole number of at least 1, not "${value}"`);
      }
      options[name.slice(2)] = number;
    } else if (name === '--classes') {
      const classes = value.split(',').map((word) => word.trim()).filter(Boolean);
      if (classes.length === 0 || classes.some((word) => !/^[A-Za-z0-9]+Tests$/.test(word))) {
        throw new Error(`--classes takes test class names separated by commas, not "${value}"`);
      }
      options.classes = classes;
    } else if (name === '--mode') {
      if (value !== 'fast' && value !== 'precise') {
        throw new Error(`--mode is fast or precise, not "${value}"`);
      }
      options.mode = value;
    } else if (name === '--only') {
      const chosen = value.split(',').map((word) => (word === 'quiet' ? word : Number(word)));
      if (chosen.some((n) => n !== 'quiet' && (!Number.isInteger(n) || n < 0))) {
        throw new Error(
          `--only takes shard numbers from 0 and the word quiet, separated by commas, not "${value}"`);
      }
      options.only = chosen;
    } else {
      options[name.slice(2)] = value;
    }
  }
  if (options.only?.some((n) => n !== 'quiet' && n >= options.count)) {
    throw new Error(`--only names a shard the deal does not have: there are ${options.count}, numbered from 0`);
  }
  if (options.classes && options.affected) {
    throw new Error('--classes names the classes and --affected has them worked out; say one of them');
  }
  return options;
}

/**
 * The mode a run is in: what it was told on the command line, or failing that what the
 * environment says, or failing that precise.
 *
 * Told two different things, it refuses. A run labelled one way and executed the other is the one
 * mistake here that nothing downstream could notice.
 */
export function modeOf(options, environment = process.env) {
  const fromEnvironment = (environment.SILEXGIS_TEST_MODE ?? '').trim().toLowerCase() || null;
  if (fromEnvironment !== null && fromEnvironment !== 'fast' && fromEnvironment !== 'precise') {
    throw new Error(`SILEXGIS_TEST_MODE is fast or precise, not "${environment.SILEXGIS_TEST_MODE}"`);
  }
  if (options.mode && fromEnvironment && options.mode !== fromEnvironment) {
    throw new Error(
      `--mode ${options.mode} was asked for while SILEXGIS_TEST_MODE says ${fromEnvironment}; say one of them`);
  }
  return options.mode ?? fromEnvironment ?? 'precise';
}

/**
 * What the test processes say they did, added up: applications built, and times a test was
 * handed one already running. Null where a process left no account — an older test project, or
 * one that died.
 */
export function hostsOf(accounts) {
  const read = accounts.filter((account) => account !== null);
  if (read.length === 0) return null;
  const because = {};
  const classes = {};
  for (const account of read) {
    for (const [reason, count] of Object.entries(account.because ?? {})) {
      because[reason] = (because[reason] ?? 0) + count;
    }
    for (const [name, made] of Object.entries(account.classes ?? {})) {
      const sum = (classes[name] ??= { built: 0, borrowed: 0, because: {} });
      sum.built += made.built ?? 0;
      sum.borrowed += made.borrowed ?? 0;
      for (const [reason, count] of Object.entries(made.because ?? {})) {
        sum.because[reason] = (sum.because[reason] ?? 0) + count;
      }
    }
  }
  return {
    processes: read.length,
    missing: accounts.length - read.length,
    built: read.reduce((sum, account) => sum + (account.built ?? 0), 0),
    borrowed: read.reduce((sum, account) => sum + (account.borrowed ?? 0), 0),
    modes: [...new Set(read.map((account) => account.mode))],
    because,
    classes,
  };
}

/**
 * Where a fast run still built applications, in lines for the end of its output: the reasons,
 * most frequent first, and the classes that built the most.
 *
 * A fast run is as quick as the number of applications it did not build. This is the list of
 * the ones it did, so that the next thing worth making shareable is read off a run rather than
 * guessed at.
 */
export function whereApplicationsWereBuilt(hosts, most = 8) {
  const byCount = (a, b) => b[1] - a[1] || a[0].localeCompare(b[0]);
  const lines = Object.entries(hosts.because ?? {})
    .sort(byCount)
    .map(([reason, count]) => `  ${String(count).padStart(5)}  ${reason}`);
  const builders = Object.entries(hosts.classes ?? {})
    .map(([name, made]) => [name, made.built, made])
    .filter(([, built]) => built > 1)
    .sort(byCount)
    .slice(0, most);
  for (const [name, built, made] of builders) {
    const why = Object.entries(made.because).sort(byCount).map(([reason, count]) => `${count} ${reason}`);
    lines.push(`  ${String(built).padStart(5)}  built by ${name} (handed one ${made.borrowed} times): ${why.join('; ')}`);
  }
  return lines;
}

/**
 * What is wrong with a run's account of itself, in words, or null when nothing is.
 *
 * A fast run of the whole suite in which no test was ever handed a running application did not do
 * what it was asked; a precise run in which one was, or a process that ran in the other mode, did
 * something it was not asked. Either way the result would be read as something it is not. A fast
 * run of a few classes may honestly share nothing — one test each, or every test asking for an
 * application of its own — so only the whole suite is held to it.
 */
export function modeComplaint(mode, hosts, { whole = true } = {}) {
  if (hosts === null) return null;
  const strangers = hosts.modes.filter((ran) => ran !== mode);
  if (strangers.length > 0) {
    return `asked for ${mode}, and ${strangers.join(' and ')} is what some test processes say they ran in`;
  }
  if (mode === 'fast' && hosts.borrowed === 0 && whole) {
    return 'asked for fast, and no test was handed a running application: every one built its own';
  }
  if (mode === 'precise' && hosts.borrowed > 0) {
    return `asked for precise, and ${hosts.borrowed} tests were handed an application another test had used`;
  }
  return null;
}

const elapsed = (sinceMs) => {
  const minutes = Math.round((Date.now() - sinceMs) / 60000);
  return minutes >= 60 ? `${Math.floor(minutes / 60)} h ${minutes % 60} min` : `${minutes} min`;
};

/** One `dotnet test` over `classes`, its output kept in `<name>.log`; resolves when it ends. */
function run({ name, classes, project, results, threads, startedAt, mode }) {
  const log = join(results, `${name}.log`);
  const account = join(results, `${name}.hosts.json`);
  rmSync(account, { force: true });
  const out = createWriteStream(log);
  const child = spawn(
    'dotnet',
    [
      'test', project, '--no-build',
      '--filter', filterExpression(classes),
      '--results-directory', results,
      '--logger', `trx;LogFileName=${name}.trx`,
      ...(threads ? ['--', `xUnit.MaxParallelThreads=${threads}`] : []),
    ],
    {
      stdio: ['ignore', 'pipe', 'pipe'],
      env: { ...process.env, SILEXGIS_TEST_MODE: mode, SILEXGIS_TEST_HOST_STATS: account },
    },
  );
  child.stdout.pipe(out, { end: false });
  child.stderr.pipe(out, { end: false });
  return new Promise((done) => {
    child.on('error', (error) => {
      console.log(`${name}: could not start (${error.message})`);
      done({ name, log, account, exitCode: 1 });
    });
    child.on('close', (code) => {
      out.end();
      console.log(`${name}: exit ${code ?? 1} after ${elapsed(startedAt)} (${classes.length} classes)`);
      done({ name, log, account, exitCode: code ?? 1 });
    });
  });
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  const mode = modeOf(options);
  const project = resolve(options.project ?? join(testDir, 'SilexGis.Api.Tests.csproj'));
  if (!existsSync(project)) {
    throw new Error(`no test project at ${project}`);
  }
  const results = resolve(options.results ?? join(dirname(project), 'TestResults', 'shards'));
  const wanted = (which) => !options.only || options.only.includes(which);
  const every = listClasses(dirname(project));
  let named = options.classes;
  if (options.affected) {
    const selection = await selectionFor(options.affected);
    console.log(
      `against ${selection.base}: ${selection.changed} changed files select `
        + (selection.mode === 'targeted' ? `${selection.classes.length} classes` : selection.mode === 'full' ? 'the whole suite' : 'no class'),
    );
    for (const reason of selection.reasons) console.log(`  - ${reason}`);
    if (selection.mode === 'full') {
      console.log(
        'The whole suite is not run under this name: run it without --affected, under '
          + '`gate-lock.mjs run --full`.',
      );
      process.exit(10);
    }
    if (selection.mode === 'none') {
      console.log('Nothing to run: the change is outside what the integration classes answer for.');
      process.exit(0);
    }
    named = selection.classes;
  }
  const unknown = (named ?? []).filter((name) => !every.includes(name));
  if (unknown.length > 0) {
    // A name that matches nothing would otherwise run nothing and be reported as a pass.
    throw new Error(`no such test class beside ${project}: ${unknown.join(', ')}`);
  }
  const all = named ? every.filter((name) => named.includes(name)) : every;
  const quiet = quietClasses(dirname(project), all);
  const dealt = all.filter((name) => !quiet.includes(name));
  const count = sharesFor(options.count, dealt.length, { restricted: named !== null, mode });
  if (options.only?.some((n) => n !== 'quiet' && n >= count)) {
    throw new Error(`--only names a shard this deal does not have: these classes make ${count}, numbered from 0`);
  }
  const shards = Array.from({ length: count }, (_, index) => index).filter(wanted);
  const threads = options.threads ?? threadsFor(Math.max(1, shards.length));
  mkdirSync(results, { recursive: true });

  console.log(
    `${dealt.length} classes in ${count} shards`
      + (options.only ? `, of which only ${options.only.join(', ')} run` : '')
      + `; ${threads} test threads each; ${quiet.length} more run alone afterwards`
      + `; results in ${results}`,
  );
  console.log(`test mode: ${mode}`);
  const startedAt = Date.now();

  const running = [];
  for (const index of shards) {
    const classes = shard(dealt, index, count);
    if (classes.length === 0) continue;
    running.push(run({ name: `shard-${index}`, classes, project, results, threads, startedAt, mode }));
    // A moment between starts: each shard asks the container runtime for a database server of its
    // own as its first act, and eight such requests in one instant is the one place the shards
    // would otherwise contend before any test has run.
    await new Promise((wait) => setTimeout(wait, 3000));
  }
  const ended = await Promise.all(running);
  if (shards.length > 0) {
    console.log(`all shards ended after ${elapsed(startedAt)}`);
  }

  if (wanted('quiet') && quiet.length > 0) {
    // Its own thread count is the suite's: nothing is beside it now, in this process or another.
    ended.push(
      await run({ name: 'quiet', classes: quiet, project, results, threads: null, startedAt, mode }));
  }

  console.log(`\neverything ended after ${elapsed(startedAt)}`);
  const hosts = hostsOf(
    ended.map(({ account }) => {
      try {
        return JSON.parse(readFileSync(account, 'utf8'));
      } catch {
        return null;
      }
    }),
  );
  if (hosts === null) {
    console.log(`test mode: ${mode} (the test processes left no account of the applications they built)`);
  } else {
    console.log(
      `test mode: ${mode} — ${hosts.built} applications built, a running one handed to a test `
        + `${hosts.borrowed} times, by the account of ${hosts.processes} test processes`
        + (hosts.missing > 0 ? ` (${hosts.missing} left none)` : ''),
    );
  }
  if (hosts !== null && mode === 'fast') {
    console.log('applications built, by why they could not be one already running:');
    for (const line of whereApplicationsWereBuilt(hosts)) console.log(line);
  }
  const complaint = modeComplaint(mode, hosts, { whole: named === null && !options.only });
  if (complaint) {
    console.log(`THE MODE IS NOT WHAT WAS ASKED FOR: ${complaint}`);
  }
  let failed = ended.length === 0 || complaint !== null;
  for (const { name, log, exitCode } of ended) {
    const summary = summaryLineOf(readFileSync(log, 'utf8'));
    if (summary) {
      console.log(summary);
    } else {
      console.log(`${name}: the runner printed no summary (exit ${exitCode}); see ${log}`);
    }
    if (exitCode !== 0 || !summary) failed = true;
  }
  process.exit(failed ? 1 : 0);
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  main().catch((error) => {
    console.error(error.message);
    process.exit(2);
  });
}
