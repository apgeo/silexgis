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
//                                 [--only 2,5,quiet]
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
import { createWriteStream, existsSync, mkdirSync, readFileSync } from 'node:fs';
import { availableParallelism } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import process from 'node:process';

import { filterExpression } from './gate-affected.mjs';
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
  return Math.max(2, Math.floor(cores / count));
}

/** Reads `--name value` options; anything it does not know is an error rather than a guess. */
export function parseArguments(argv) {
  const options = { count: 8, threads: null, results: null, project: null, only: null };
  for (let index = 0; index < argv.length; index += 1) {
    const name = argv[index];
    const value = argv[index + 1];
    if (!['--count', '--threads', '--results', '--project', '--only'].includes(name) || value === undefined) {
      throw new Error(`unknown or incomplete argument: ${name}`);
    }
    index += 1;
    if (name === '--count' || name === '--threads') {
      const number = Number(value);
      if (!Number.isInteger(number) || number < 1) {
        throw new Error(`${name} takes a whole number of at least 1, not "${value}"`);
      }
      options[name.slice(2)] = number;
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
  return options;
}

const elapsed = (sinceMs) => {
  const minutes = Math.round((Date.now() - sinceMs) / 60000);
  return minutes >= 60 ? `${Math.floor(minutes / 60)} h ${minutes % 60} min` : `${minutes} min`;
};

/** One `dotnet test` over `classes`, its output kept in `<name>.log`; resolves when it ends. */
function run({ name, classes, project, results, threads, startedAt }) {
  const log = join(results, `${name}.log`);
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
    { stdio: ['ignore', 'pipe', 'pipe'] },
  );
  child.stdout.pipe(out, { end: false });
  child.stderr.pipe(out, { end: false });
  return new Promise((done) => {
    child.on('error', (error) => {
      console.log(`${name}: could not start (${error.message})`);
      done({ name, log, exitCode: 1 });
    });
    child.on('close', (code) => {
      out.end();
      console.log(`${name}: exit ${code ?? 1} after ${elapsed(startedAt)} (${classes.length} classes)`);
      done({ name, log, exitCode: code ?? 1 });
    });
  });
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  const project = resolve(options.project ?? join(testDir, 'SilexGis.Api.Tests.csproj'));
  if (!existsSync(project)) {
    throw new Error(`no test project at ${project}`);
  }
  const results = resolve(options.results ?? join(dirname(project), 'TestResults', 'shards'));
  const wanted = (which) => !options.only || options.only.includes(which);
  const all = listClasses(dirname(project));
  const quiet = quietClasses(dirname(project), all);
  const dealt = all.filter((name) => !quiet.includes(name));
  const shards = Array.from({ length: options.count }, (_, index) => index).filter(wanted);
  const threads = options.threads ?? threadsFor(Math.max(1, shards.length));
  mkdirSync(results, { recursive: true });

  console.log(
    `${dealt.length} classes in ${options.count} shards`
      + (options.only ? `, of which only ${options.only.join(', ')} run` : '')
      + `; ${threads} test threads each; ${quiet.length} more run alone afterwards`
      + `; results in ${results}`,
  );
  const startedAt = Date.now();

  const running = [];
  for (const index of shards) {
    const classes = shard(dealt, index, options.count);
    if (classes.length === 0) continue;
    running.push(run({ name: `shard-${index}`, classes, project, results, threads, startedAt }));
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
    ended.push(await run({ name: 'quiet', classes: quiet, project, results, threads: null, startedAt }));
  }

  console.log(`\neverything ended after ${elapsed(startedAt)}`);
  let failed = ended.length === 0;
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
