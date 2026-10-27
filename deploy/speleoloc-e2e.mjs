// SPDX-License-Identifier: AGPL-3.0-or-later
//
// The SpeleoLoc app's live sync tests, run against a SilexGIS server built from this checkout.
//
// Cross-platform (Node 18+, no shell-isms). One command:
//
//   node deploy/speleoloc-e2e.mjs --app <SpeleoLoc checkout> [options]
//
// It throws the development server's data away (`speleoloc-dev.mjs reset`), stands it up from
// this checkout (`speleoloc-dev.mjs up`, which seeds it and proves the protection rules before it
// reports success), runs the app's live tests against it with `flutter test`, stops the server,
// and writes a short report naming both commits and the counts. Exit code 0 means every live test
// ran and passed; 1 means a test failed or none ran; 2 means the server never came up.
//
// Options:
//   --app DIR          the SpeleoLoc checkout (or SPELEOLOC_CHECKOUT)
//   --tests A,B        test files, relative to the app checkout. Default: every file under its
//                      test/ that reads SPELEO_LOC_SILEXGIS_LIVE, which is how a live test opts in
//   --runs-dir DIR     where the report goes (or SPELEOLOC_E2E_RUNS_DIR). Default .speleoloc-e2e/
//   --keep-data        skip the reset and run against the data the last `up` left
//   --flutter CMD      the flutter command (or FLUTTER). Default `flutter` on the PATH
//   --timeout-minutes  for the test run, default 30
//   --db-port N, --api-port N   as for speleoloc-dev.mjs
//
// WHY THE REPORT HOLDS COUNTS AND NOT THE OUTPUT
// The report is meant to be passed between the two efforts and read by people who did not run
// it. It names the commit pair, the counts and each failed test with the first line of its error;
// the whole server log and the whole test output stay in the run's log directory beside it.
//
// WHY ZERO TESTS IS A FAILURE
// A live test that does not see its switch passes as a placeholder, and a test selection that
// matched nothing runs nothing — both exit zero. A cross-repository check that turns green on
// either says nothing about the API, so a run in which no live test actually executed is red.
//
// WHY IT RESETS BY DEFAULT
// The database volume is shared by every checkout that runs the development server, while the
// stored files and keys sit beside the checkout that wrote them. A run on another checkout's
// leftovers can fail on a missing file that has nothing to do with the change under test, so
// each run starts from an empty installation unless told otherwise.

import { spawn, spawnSync } from 'node:child_process';
import { once } from 'node:events';
import { createWriteStream, existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import {
  DEFAULT_API_PORT,
  DEFAULT_DB_PORT,
  baseUrlFor,
  detachApi,
  parseArgs,
  stopApiTree,
} from './speleoloc-dev.mjs';

const deployDir = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(deployDir, '..');
const DEV_SCRIPT = join(deployDir, 'speleoloc-dev.mjs');

/** Git ignores it. Not inside `.speleoloc-dev/`, which `reset` deletes. */
export const DEFAULT_RUNS_DIR = join(repoRoot, '.speleoloc-e2e');

/** The switch every live test in the app reads; its presence is what marks a file as one. */
export const LIVE_SWITCH = 'SPELEO_LOC_SILEXGIS_LIVE';

/** What `up` prints once the server is seeded, signed into and proved — and only then. */
export const UP_READY_MARKER = '--- a SilexGIS server for the SpeleoLoc sync API';

/** The placeholder a live test registers when its switch is off. It passes, so it must not count. */
const PLACEHOLDER = new RegExp(`skipped \\(set ${LIVE_SWITCH}=1\\)`);

/**
 * The app's live test files, relative to its checkout, in a stable order. A file is a live test
 * when it reads the switch; a test that talks to a server without reading it would run against
 * whatever answers on the default port, which is the thing this script exists to control.
 */
export function liveTestFiles(appDir, { read = (p) => readFileSync(p, 'utf8') } = {}) {
  const root = join(appDir, 'test');
  const found = [];
  const walk = (dir) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const path = join(dir, entry.name);
      if (entry.isDirectory()) {
        walk(path);
      } else if (entry.name.endsWith('_test.dart') && read(path).includes(LIVE_SWITCH)) {
        found.push(relative(appDir, path).split('\\').join('/'));
      }
    }
  };
  if (existsSync(root)) {
    walk(root);
  }
  return found.sort();
}

/**
 * Counts out of `flutter test --reporter json`. Lines that are not JSON (dependency resolution,
 * the tool's own notices) are ignored. Hidden tests are the runner's own loading and set-up
 * steps; one that fails is not hidden, so a file that does not compile counts as a failure.
 */
export function summarizeFlutterJson(text) {
  const tests = new Map();
  const errors = new Map();
  let done = null;
  for (const line of text.split(/\r?\n/)) {
    if (!line.startsWith('{')) {
      continue;
    }
    let event;
    try {
      event = JSON.parse(line);
    } catch {
      continue;
    }
    if (event.type === 'testStart') {
      tests.set(event.test.id, { name: event.test.name, url: event.test.url ?? null });
    } else if (event.type === 'error' && !errors.has(event.testID)) {
      errors.set(event.testID, String(event.error ?? ''));
    } else if (event.type === 'testDone') {
      const test = tests.get(event.testID);
      if (test) {
        Object.assign(test, { result: event.result, skipped: event.skipped, hidden: event.hidden });
      }
    } else if (event.type === 'done') {
      done = event.success;
    }
  }

  const summary = { passed: 0, failed: 0, skipped: 0, placeholders: 0, failures: [], runnerSuccess: done };
  for (const [id, test] of tests) {
    if (test.result === undefined) {
      // Started and never finished: the run was cut off underneath it.
      summary.failed += 1;
      summary.failures.push({ name: test.name, error: 'did not finish' });
    } else if (test.hidden && test.result === 'success') {
      continue;
    } else if (test.skipped) {
      summary.skipped += 1;
    } else if (test.result === 'success') {
      if (PLACEHOLDER.test(test.name)) {
        summary.placeholders += 1;
      } else {
        summary.passed += 1;
      }
    } else {
      summary.failed += 1;
      summary.failures.push({ name: test.name, error: firstLine(errors.get(id) ?? test.result) });
    }
  }
  return summary;
}

const firstLine = (text) => {
  const line = String(text).split(/\r?\n/).find((l) => l.trim() !== '') ?? '';
  return line.length > 300 ? `${line.slice(0, 297)}…` : line;
};

/** Green only when something real ran and nothing failed. */
export function verdictOf(summary) {
  if (summary.failed > 0) {
    return 'red';
  }
  if (summary.passed === 0) {
    return 'red: no live test ran';
  }
  return 'green';
}

/** `2026-10-28T09-15Z-silexgis-4610f41a-speleoloc-99a5d23f`: sortable, and names the pair. */
export function runName({ startedAt, silexgis, app }) {
  const stamp = startedAt.toISOString().slice(0, 16).replace(':', '-');
  return `${stamp}Z-silexgis-${silexgis.commit}-speleoloc-${app.commit}`;
}

export function renderReport({ startedAt, seconds, silexgis, app, files, summary, setupProblem, logDir }) {
  const verdict = setupProblem ? 'not run: the server did not come up' : verdictOf(summary);
  const lines = [
    `# SpeleoLoc live sync tests against SilexGIS — ${verdict}`,
    '',
    `- **When:** ${startedAt.toISOString()} · ${Math.round(seconds)} s`,
    `- **SilexGIS:** \`${silexgis.commit}\`${silexgis.dirty ? ' + uncommitted changes' : ''}`,
    `- **SpeleoLoc:** \`${app.commit}\`${app.dirty ? ' + uncommitted changes' : ''}`,
    `- **Test files:** ${files.length ? files.map((f) => `\`${f}\``).join(', ') : 'none found'}`,
    `- **Logs:** \`${logDir}\``,
    '',
  ];
  if (setupProblem) {
    lines.push('## Why the server did not come up', '', setupProblem, '');
    return lines.join('\n');
  }
  lines.push(
    '| Passed | Failed | Skipped | Placeholders (switch not seen) |',
    '|---|---|---|---|',
    `| ${summary.passed} | ${summary.failed} | ${summary.skipped} | ${summary.placeholders} |`,
    '',
  );
  if (summary.failures.length) {
    lines.push('## Failed', '');
    for (const failure of summary.failures) {
      lines.push(`- **${failure.name}** — ${failure.error}`);
    }
    lines.push('');
  }
  return lines.join('\n');
}

function commitOf(dir) {
  const head = spawnSync('git', ['-C', dir, 'rev-parse', '--short=8', 'HEAD'], { encoding: 'utf8' });
  const status = spawnSync('git', ['-C', dir, 'status', '--porcelain'], { encoding: 'utf8' });
  return {
    commit: head.status === 0 ? head.stdout.trim() : 'unknown',
    dirty: status.status === 0 && status.stdout.trim() !== '',
  };
}

/** Any HTTP answer at all on the API port means a server this run did not start. */
async function somethingAnswers(baseUrl) {
  try {
    await fetch(`${baseUrl}/api/v1/sync/capabilities`, { signal: AbortSignal.timeout(3000) });
    return true;
  } catch {
    return false;
  }
}

function fail(message, code = 2) {
  console.error(`\n${message}`);
  process.exit(code);
}

/** Starts `up` with its output in a log file; resolves when it has proved the server, or with why not. */
function startServer(portArgs, logFile) {
  const log = createWriteStream(logFile);
  const child = spawn(process.execPath, [DEV_SCRIPT, 'up', ...portArgs], {
    stdio: ['ignore', 'pipe', 'pipe'],
    detached: detachApi(),
  });
  let tail = '';
  const ready = new Promise((done) => {
    const timer = setTimeout(() => done('`up` did not report a proved server within fifteen minutes.'), 900_000);
    for (const stream of [child.stdout, child.stderr]) {
      stream.setEncoding('utf8');
      stream.on('data', (chunk) => {
        log.write(chunk);
        tail = (tail + chunk).slice(-4000);
        if (tail.includes(UP_READY_MARKER)) {
          clearTimeout(timer);
          done(null);
        }
      });
    }
    child.on('exit', (code) => {
      clearTimeout(timer);
      done(`\`speleoloc-dev.mjs up\` exited with ${code} before proving the server. The last of its `
        + `output:\n\n\`\`\`\n${tail.slice(-1500)}\n\`\`\``);
    });
  });
  return { child, ready, log };
}

/**
 * Runs the tests without blocking this process. It must not be `spawnSync`: the server's output
 * reaches this process through a pipe, and while the event loop is blocked nothing drains it —
 * the pipe fills, the server blocks on its next log line, and every request the tests make times
 * out against a server that looks alive.
 */
function runFlutter({ flutter, appDir, files, baseUrl, timeoutMinutes, logFile }) {
  const args = ['test', '--reporter', 'json', '--concurrency', '1', ...files];
  console.log(`\n$ (in ${appDir}) ${flutter} ${args.join(' ')}`);
  return new Promise((done) => {
    const chunks = [];
    const child = spawn(flutter, args, {
      cwd: appDir,
      // flutter is a batch file on Windows, which spawn only runs through a shell.
      shell: process.platform === 'win32',
      env: { ...process.env, [LIVE_SWITCH]: '1', SPELEO_LOC_SILEXGIS_URL: baseUrl },
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    for (const stream of [child.stdout, child.stderr]) {
      stream.setEncoding('utf8');
      stream.on('data', (chunk) => chunks.push(chunk));
    }
    let problem = null;
    const timer = setTimeout(() => {
      problem = `the test run did not finish within ${timeoutMinutes} minutes`;
      child.kill();
    }, timeoutMinutes * 60_000);
    child.on('error', (error) => { problem = `Could not run ${flutter}: ${error.message}`; });
    child.on('close', () => {
      clearTimeout(timer);
      const output = chunks.join('');
      writeFileSync(logFile, output);
      done({ output, problem });
    });
  });
}

async function main(options) {
  const appDir = resolve(String(options.app ?? process.env.SPELEOLOC_CHECKOUT ?? ''));
  if (!options.app && !process.env.SPELEOLOC_CHECKOUT) {
    fail('Name the SpeleoLoc checkout: --app <dir> (or SPELEOLOC_CHECKOUT).', 2);
  }
  if (!existsSync(join(appDir, 'pubspec.yaml'))) {
    fail(`${appDir} has no pubspec.yaml; --app must be the SpeleoLoc checkout.`, 2);
  }
  const dbPort = Number(options['db-port'] ?? DEFAULT_DB_PORT);
  const apiPort = Number(options['api-port'] ?? DEFAULT_API_PORT);
  const portArgs = ['--db-port', String(dbPort), '--api-port', String(apiPort)];
  const baseUrl = baseUrlFor(apiPort);
  const flutter = String(options.flutter ?? process.env.FLUTTER ?? 'flutter');
  const timeoutMinutes = Number(options['timeout-minutes'] ?? 30);
  const runsDir = resolve(String(options['runs-dir'] ?? process.env.SPELEOLOC_E2E_RUNS_DIR ?? DEFAULT_RUNS_DIR));

  const files = typeof options.tests === 'string'
    ? options.tests.split(',').map((f) => f.trim()).filter(Boolean)
    : liveTestFiles(appDir);
  const startedAt = new Date();
  const silexgis = commitOf(repoRoot);
  const app = commitOf(appDir);
  const name = runName({ startedAt, silexgis, app });
  const logDir = join(DEFAULT_RUNS_DIR, 'logs', name);
  mkdirSync(logDir, { recursive: true });
  mkdirSync(runsDir, { recursive: true });

  const finish = (summary, setupProblem) => {
    const seconds = (Date.now() - startedAt.getTime()) / 1000;
    const report = renderReport({ startedAt, seconds, silexgis, app, files, summary, setupProblem, logDir });
    const reportFile = join(runsDir, `${name}.md`);
    writeFileSync(reportFile, `${report}\n`);
    console.log(`\n${report}\nReport: ${reportFile}`);
    if (setupProblem) {
      return 2;
    }
    return verdictOf(summary) === 'green' ? 0 : 1;
  };

  if (files.length === 0) {
    return finish({ passed: 0, failed: 0, skipped: 0, placeholders: 0, failures: [] }, null);
  }
  if (await somethingAnswers(baseUrl)) {
    return finish(null, `Something already answers on ${baseUrl}. This run stands up its own server `
      + 'from this checkout and will not test against one it did not start: stop that one '
      + '(Ctrl-C in its terminal, then `node deploy/speleoloc-dev.mjs down`), or pass other ports.');
  }

  if (!options['keep-data']) {
    const reset = spawnSync(process.execPath, [DEV_SCRIPT, 'reset', ...portArgs], { encoding: 'utf8' });
    writeFileSync(join(logDir, 'reset.log'), `${reset.stdout ?? ''}${reset.stderr ?? ''}`);
    if (reset.status !== 0) {
      return finish(null, `\`speleoloc-dev.mjs reset\` exited with ${reset.status}; see \`${logDir}/reset.log\`.`);
    }
  }

  console.log(`Standing the server up on ${baseUrl} (log: ${join(logDir, 'server.log')}) …`);
  const server = startServer(portArgs, join(logDir, 'server.log'));
  const stopServer = async () => {
    if (server.child.exitCode === null && server.child.signalCode === null) {
      stopApiTree(server.child);
      await Promise.race([once(server.child, 'exit'), new Promise((done) => setTimeout(done, 30_000))]);
    }
    server.log.end();
    spawnSync(process.execPath, [DEV_SCRIPT, 'down', ...portArgs], { stdio: 'ignore' });
  };
  process.on('SIGINT', () => { stopApiTree(server.child); process.exit(130); });

  const setupProblem = await server.ready;
  if (setupProblem) {
    await stopServer();
    return finish(null, setupProblem);
  }

  const { output, problem } = await runFlutter({
    flutter, appDir, files, baseUrl, timeoutMinutes, logFile: join(logDir, 'flutter.jsonl'),
  });
  await stopServer();
  const summary = summarizeFlutterJson(output);
  if (problem) {
    summary.failed += 1;
    summary.failures.push({ name: 'flutter test', error: problem });
  } else if (summary.runnerSuccess === null) {
    summary.failed += 1;
    summary.failures.push({ name: 'flutter test', error: 'the run ended without its final event (timed out or crashed)' });
  }
  return finish(summary, null);
}

// Only when run, not when imported, so the test file can exercise the helpers above.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const options = parseArgs(process.argv.slice(2));
  if (options.help) {
    const head = readFileSync(fileURLToPath(import.meta.url), 'utf8').split('\n').slice(2, 23);
    console.log(head.map((l) => l.replace(/^\/\/ ?/, '')).join('\n'));
    process.exit(0);
  }
  process.exit(await main(options));
}
