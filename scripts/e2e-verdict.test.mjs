// SPDX-License-Identifier: AGPL-3.0-or-later
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

import {
  ATTEMPTS,
  NETWORK_CHANGED,
  canBeJudged,
  judgeRun,
  outcomesOf,
  selectorsByProject,
  sortFailures,
  sortSecondRun,
  verdict,
  withReport,
} from './e2e-verdict.mjs';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');

/** One test as Playwright's report writes it. */
function reported({ title, line, status = 'expected', project = 'desktop', marks = [] }) {
  return {
    title,
    line,
    column: 1,
    tests: [
      {
        projectName: project,
        status,
        annotations: [],
        results: [
          { status: status === 'unexpected' ? 'failed' : 'passed', annotations: marks, retry: 0 },
        ],
      },
    ],
  };
}

const cutOffMark = [{ type: NETWORK_CHANGED, description: '5 request(s) were cut off' }];

/** A report of one spec file, with an optional described group inside it. */
function reportOf(file, specs, { group, errors = [] } = {}) {
  const located = specs.map((spec) => ({ ...spec, file }));
  return {
    errors,
    suites: [
      group
        ? { title: file, file, specs: [], suites: [{ title: group, file, specs: located }] }
        : { title: file, file, specs: located },
    ],
  };
}

test('a failure is set aside only when the browser said the network changed during it', () => {
  const report = reportOf('calendar.spec.ts', [
    reported({ title: 'passes', line: 10 }),
    reported({ title: 'fails on its own', line: 20, status: 'unexpected' }),
    reported({ title: 'was cut off', line: 30, status: 'unexpected', marks: cutOffMark }),
    // The network changed and the test passed all the same: nothing to judge.
    reported({ title: 'was cut off and passed', line: 40, marks: cutOffMark }),
    // Declared `test.fail` and failed, which is what was expected of it.
    reported({ title: 'expected to fail', line: 50, status: 'expected' }),
    reported({ title: 'skipped', line: 60, status: 'skipped' }),
  ]);

  const { standing, cutOff, outside } = sortFailures(report);

  assert.deepEqual(standing.map((t) => t.title), ['fails on its own']);
  assert.deepEqual(cutOff.map((t) => t.title), ['was cut off']);
  assert.equal(outside, 0);
});

test('a test inside a described group is named with its group, and found again by it', () => {
  const first = reportOf(
    'annotated-text.spec.ts',
    [reported({ title: 'marks a passage', line: 115, status: 'unexpected', marks: cutOffMark })],
    { group: 'link-annotated text' },
  );
  const [test_] = sortFailures(first).cutOff;
  assert.equal(test_.title, 'link-annotated text › marks a passage');

  const again = reportOf(
    'annotated-text.spec.ts',
    [reported({ title: 'marks a passage', line: 115 })],
    { group: 'link-annotated text' },
  );
  assert.deepEqual(sortSecondRun([test_], again).passed, [test_]);
});

test('the second run is the verdict on what it was started for, and on nothing else', () => {
  const pending = sortFailures(
    reportOf('trips.spec.ts', [
      reported({ title: 'passes again', line: 10, status: 'unexpected', marks: cutOffMark }),
      reported({ title: 'fails again, on its own', line: 20, status: 'unexpected', marks: cutOffMark }),
      reported({ title: 'is cut off again', line: 30, status: 'unexpected', marks: cutOffMark }),
      reported({ title: 'is never run', line: 40, status: 'unexpected', marks: cutOffMark }),
    ]),
  ).cutOff;

  const again = reportOf('trips.spec.ts', [
    reported({ title: 'passes again', line: 10 }),
    reported({ title: 'fails again, on its own', line: 20, status: 'unexpected' }),
    reported({ title: 'is cut off again', line: 30, status: 'unexpected', marks: cutOffMark }),
    // Shares a line with a test that was asked for, as the tests of a loop do, and fails. It was
    // not one of the failures being judged and must not become one here.
    reported({ title: 'a neighbour on the same line', line: 10, status: 'unexpected' }),
  ]);

  const { passed, standing, cutOff } = sortSecondRun(pending, again);

  assert.deepEqual(passed.map((t) => t.title), ['passes again']);
  assert.deepEqual(standing.map((t) => t.title), ['fails again, on its own']);
  // Cut off again, and not run at all: neither has been judged.
  assert.deepEqual(cutOff.map((t) => t.title), ['is cut off again', 'is never run']);
});

test('the same test in two projects is two tests', () => {
  const report = {
    suites: [
      {
        title: 'scene3d-mobile.spec.ts',
        file: 'scene3d-mobile.spec.ts',
        specs: ['mobile-android', 'mobile-android-landscape'].map((project) => ({
          ...reported({
            title: 'opens the scene',
            line: 12,
            project,
            status: 'unexpected',
            marks: project === 'mobile-android' ? cutOffMark : [],
          }),
          file: 'scene3d-mobile.spec.ts',
        })),
      },
    ],
  };

  const { standing, cutOff } = sortFailures(report);

  assert.deepEqual(cutOff.map((t) => t.project), ['mobile-android']);
  assert.deepEqual(standing.map((t) => t.project), ['mobile-android-landscape']);
});

test('tests are asked for by the place they are written, once each, per project', () => {
  const selectors = selectorsByProject([
    { project: 'desktop', file: 'mobile.spec.ts', line: 37, title: 'a' },
    { project: 'desktop', file: 'mobile.spec.ts', line: 37, title: 'b' },
    { project: 'desktop', file: 'trips.spec.ts', line: 5, title: 'c' },
    { project: 'mobile-android', file: 'mobile.spec.ts', line: 37, title: 'a' },
  ]);

  assert.deepEqual(selectors.get('desktop'), ['/mobile.spec.ts:37', '/trips.spec.ts:5']);
  assert.deepEqual(selectors.get('mobile-android'), ['/mobile.spec.ts:37']);
});

test('a selector names one file even where another file ends in the same name', () => {
  // What Playwright does with a selector: the part before the line is a pattern looked for
  // anywhere in the file's path.
  const selects = (selector, path) => new RegExp(selector.replace(/:\d+$/, ''), 'gi').test(path);
  const [selector] = selectorsByProject([
    { project: 'desktop', file: 'mobile.spec.ts', line: 37, title: 'a' },
  ]).get('desktop');

  assert.equal(selects(selector, '/repo/client/e2e/mobile.spec.ts'), true);
  assert.equal(selects(selector, '/repo/client/e2e/scene3d-mobile.spec.ts'), false);
});

test('the report is added to the reporter that was asked for, and to the default when none was', () => {
  assert.deepEqual(withReport(['smoke', '--project=desktop'], false), [
    'smoke',
    '--project=desktop',
    '--reporter=list,json',
  ]);
  assert.deepEqual(withReport(['smoke'], true), ['smoke', '--reporter=dot,json']);
  assert.deepEqual(withReport(['--reporter=github,list'], true), ['--reporter=github,list,json']);
  assert.deepEqual(withReport(['--reporter', 'line', 'smoke'], false), ['--reporter=line,json', 'smoke']);
  // Already asked for: named once, not twice.
  assert.deepEqual(withReport(['--reporter=json,list'], false), ['--reporter=json,list']);
});

test('a run a person is steering, one repeating each test, or one that stops early is left as Playwright judged it', () => {
  assert.equal(canBeJudged(['smoke', '--project=desktop', '--workers=3']), true);
  assert.equal(canBeJudged(['--ui']), false);
  assert.equal(canBeJudged(['--debug']), false);
  assert.equal(canBeJudged(['--repeat-each=5']), false);
  assert.equal(canBeJudged(['--repeat-each', '5']), false);
  assert.equal(canBeJudged(['--list']), false);
  // Stopped at its first failure, so most of the suite never ran: a second run of that one
  // failure says nothing about the rest.
  assert.equal(canBeJudged(['-x']), false);
  assert.equal(canBeJudged(['--max-failures=3']), false);
  assert.equal(canBeJudged(['--max-failures', '3']), false);
  // Not the same flag, whatever it starts with.
  assert.equal(canBeJudged(['--listen-on=1']), true);
});

test('only a run with nothing standing, nothing unjudged and nothing outside a test is green', () => {
  const one = [{ project: 'desktop', file: 'a.spec.ts', line: 1, title: 't' }];
  const none = { excused: [], standing: [], unjudged: [], outside: 0 };

  assert.equal(verdict({ ...none, excused: one }).exitCode, 0);
  assert.equal(verdict({ ...none, standing: one }).exitCode, 1);
  assert.equal(verdict({ ...none, unjudged: one }).exitCode, 1);
  assert.equal(verdict({ ...none, excused: one, outside: 1 }).exitCode, 1);

  const said = verdict({ ...none, excused: one, standing: one }).lines.join('\n');
  assert.match(said, /1 failure\(s\) were cut off by the network changing/);
  assert.match(said, /1 failure\(s\) stand:/);
  assert.match(said, /\[desktop\] a\.spec\.ts:1 › t/);
  assert.match(verdict({ ...none, unjudged: one }).lines.join('\n'), new RegExp(`${ATTEMPTS + 1} attempts`));
});

test('what the run reported outside any test is never set aside', () => {
  const report = reportOf(
    'a.spec.ts',
    [reported({ title: 'was cut off', line: 1, status: 'unexpected', marks: cutOffMark })],
    { errors: [{ message: 'worker process exited unexpectedly' }] },
  );

  assert.equal(sortFailures(report).outside, 1);
});

test('a report that was never written sorts to nothing rather than to green', () => {
  assert.deepEqual(outcomesOf(null), []);
  assert.deepEqual(sortFailures(null), {
    standing: [], cutOff: [], interrupted: [], flaky: [], outside: 0, unaccounted: 0,
  });
});

test('the guard marks a test with the word this reads', () => {
  // Two files in two languages, so the word is written twice. If they part, every failure reads
  // as standing and nothing here says why.
  const guard = readFileSync(join(repoRoot, 'client', 'e2e', 'consoleGuard.ts'), 'utf8');
  // Said in words rather than left to the assertion, which would print the whole file.
  const says = (pattern, what) => assert.ok(pattern.test(guard), `the guard no longer ${what}`);
  says(new RegExp(`const NETWORK_CHANGED = '${NETWORK_CHANGED}';`), `names the mark '${NETWORK_CHANGED}'`);
  says(/type: NETWORK_CHANGED\b/, 'marks the test with it');
  says(/'net::ERR_NETWORK_CHANGED'/, 'reads the browser\'s own word for a network change');
});

// ---- the exit code of a whole run ------------------------------------------------------------
//
// judgeRun is what e2e.mjs asks for its exit code. These are the paths a run can take; each one
// is a different way for a red run to be called green, which is what the function is there to stop.

/** A report of one file with the given specs and a tally in the shape Playwright writes. */
function runReport(specs, { errors = [], unexpected } = {}) {
  const report = reportOf('a.spec.ts', specs, { errors });
  report.stats = { unexpected: unexpected ?? specs.filter((s) => s.tests[0].status === 'unexpected').length };
  return report;
}

/** A rerun that must not be called; used where nothing is cut off. */
const noRerun = () => assert.fail('nothing was cut off, so nothing should have been run again');

const passing = [reported({ title: 'passes', line: 1 })];
const standingFailure = reported({ title: 'fails on its own', line: 20, status: 'unexpected' });
const cutOffFailure = reported({ title: 'was cut off', line: 30, status: 'unexpected', marks: cutOffMark });

test('a run that passed is green and runs nothing again', () => {
  const result = judgeRun({ status: 0, report: runReport(passing), rerun: noRerun });
  assert.equal(result.exitCode, 0);
  assert.deepEqual(result.lines, []);
});

test('a run with a failure of its own is red, and keeps Playwright\'s status', () => {
  const result = judgeRun({
    status: 1, report: runReport([...passing, standingFailure]), rerun: noRerun });
  assert.equal(result.exitCode, 1);
  assert.match(result.lines.join('\n'), /1 failure\(s\) stand:/);
});

test('a failure that was cut off and then passed is the only thing that may be forgiven', () => {
  const asked = [];
  const result = judgeRun({
    status: 1,
    report: runReport([...passing, cutOffFailure]),
    rerun: (project, places, attempt) => {
      asked.push({ project, places, attempt });
      return { status: 0, report: runReport([{ ...reported({ title: 'was cut off', line: 30 }) }]) };
    },
  });
  assert.equal(result.exitCode, 0);
  assert.deepEqual(asked, [{ project: 'desktop', places: ['/a.spec.ts:30'], attempt: 1 }]);
  assert.match(result.lines.join('\n'), /passed when run again on their own/);
});

test('a cut-off failure that passes does not excuse a second, ordinary failure beside it', () => {
  const result = judgeRun({
    status: 1,
    report: runReport([standingFailure, cutOffFailure]),
    rerun: () => ({ status: 0, report: runReport([reported({ title: 'was cut off', line: 30 })]) }),
  });
  assert.equal(result.exitCode, 1);
  const said = result.lines.join('\n');
  assert.match(said, /passed when run again/);
  assert.match(said, /1 failure\(s\) stand:/);
  assert.match(said, /fails on its own/);
});

test('a cut-off failure that passes does not excuse an interrupted test or an error outside any test', () => {
  const interrupted = reported({ title: 'was stopped', line: 40 });
  interrupted.tests[0].status = 'skipped';
  interrupted.tests[0].results = [{ status: 'interrupted', annotations: [] }];
  const again = () => ({ status: 0, report: runReport([reported({ title: 'was cut off', line: 30 })]) });

  const stopped = judgeRun({
    status: 1, report: runReport([cutOffFailure, interrupted]), rerun: again });
  assert.equal(stopped.exitCode, 1);
  assert.match(stopped.lines.join('\n'), /1 test\(s\) were interrupted/);

  const crashed = judgeRun({
    status: 1,
    report: runReport([cutOffFailure], { errors: [{ message: 'worker process exited unexpectedly' }] }),
    rerun: again,
  });
  assert.equal(crashed.exitCode, 1);
  assert.match(crashed.lines.join('\n'), /1 error\(s\) outside any test/);
});

test('a re-run that is interrupted or skips the test does not excuse it, and exits non-zero', () => {
  const stopped = reported({ title: 'was cut off', line: 30 });
  stopped.tests[0].status = 'skipped';
  stopped.tests[0].results = [{ status: 'interrupted', annotations: [] }];
  const interruptedAgain = judgeRun({
    status: 1, report: runReport([cutOffFailure]),
    rerun: () => ({ status: 130, report: runReport([stopped]) }),
  });
  assert.notEqual(interruptedAgain.exitCode, 0);
  assert.doesNotMatch(interruptedAgain.lines.join('\n'), /passed when run again/);

  const skipped = reported({ title: 'was cut off', line: 30 });
  skipped.tests[0].status = 'skipped';
  skipped.tests[0].results = [{ status: 'skipped', annotations: [] }];
  const skippedAgain = judgeRun({
    status: 1, report: runReport([cutOffFailure]),
    rerun: () => ({ status: 0, report: runReport([skipped]) }),
  });
  assert.notEqual(skippedAgain.exitCode, 0);

  const silentFailure = judgeRun({
    status: 1, report: runReport([cutOffFailure]),
    rerun: () => ({ status: 1, report: runReport([reported({ title: 'was cut off', line: 30 })]) }),
  });
  assert.notEqual(silentFailure.exitCode, 0);
});

test('a failing test that was cut off and fails again, ordinarily, stands', () => {
  const result = judgeRun({
    status: 1,
    report: runReport([cutOffFailure]),
    rerun: () => ({ status: 1, report: runReport([reported({ title: 'was cut off', line: 30, status: 'unexpected' })]) }),
  });
  assert.equal(result.exitCode, 1);
  assert.match(result.lines.join('\n'), /1 failure\(s\) stand:/);
});

test('a test cut off in every attempt is unjudged, and a rerun that left no report proves nothing', () => {
  let runs = 0;
  const cutAgain = judgeRun({
    status: 1,
    report: runReport([cutOffFailure]),
    rerun: () => {
      runs += 1;
      return { status: 1, report: runReport([reported({ title: 'was cut off', line: 30, status: 'unexpected', marks: cutOffMark })]) };
    },
  });
  assert.equal(cutAgain.exitCode, 1);
  assert.equal(runs, ATTEMPTS);
  assert.match(cutAgain.lines.join('\n'), /could not be judged/);

  const none = judgeRun({ status: 1, report: runReport([cutOffFailure]), rerun: () => ({ status: 1, report: null }) });
  assert.equal(none.exitCode, 1);
});

test('errors the second run reports outside any test turn a forgiven run red', () => {
  const result = judgeRun({
    status: 1,
    report: runReport([cutOffFailure]),
    rerun: () => ({
      status: 1,
      report: runReport([reported({ title: 'was cut off', line: 30 })], { errors: [{ message: 'boom' }] }),
    }),
  });
  assert.equal(result.exitCode, 1);
});

test('an interrupted run is red even though Playwright counts its tests as skipped', () => {
  const interrupted = reported({ title: 'was stopped', line: 40 });
  interrupted.tests[0].status = 'skipped';
  interrupted.tests[0].results = [{ status: 'interrupted', annotations: [] }];
  // Status zero as well: the report alone is enough.
  for (const status of [130, 1, 0]) {
    const result = judgeRun({ status, report: runReport([...passing, interrupted]), rerun: noRerun });
    assert.notEqual(result.exitCode, 0, `status ${status}`);
  }
});

test('a test that failed and then passed on a retry is red', () => {
  const flaky = reported({ title: 'passed on retry', line: 50 });
  flaky.tests[0].status = 'flaky';
  const result = judgeRun({ status: 0, report: runReport([flaky]), rerun: noRerun });
  assert.equal(result.exitCode, 1);
  assert.match(result.lines.join('\n'), /A retry is not a pass here/);
});

test('a failing status with a report that explains nothing, or no report, is red', () => {
  assert.equal(judgeRun({ status: 1, report: runReport(passing), rerun: noRerun }).exitCode, 1);
  assert.equal(judgeRun({ status: 2, report: null, rerun: noRerun }).exitCode, 2);
  assert.match(
    judgeRun({ status: 1, report: null, rerun: noRerun }).lines.join('\n'), /names nothing that explains it/);
});

test('a failure the tally counts and the walk cannot place still turns the run red', () => {
  const report = runReport(passing, { unexpected: 1 });
  const result = judgeRun({ status: 1, report, rerun: noRerun });
  assert.equal(result.exitCode, 1);
  assert.match(result.lines.join('\n'), /counts 1 more failing test/);
});

test('a run that cannot be judged keeps its cut-off failures as failures, without running them again', () => {
  const result = judgeRun({
    status: 1, report: runReport([cutOffFailure]), judgeable: false, rerun: noRerun });
  assert.equal(result.exitCode, 1);
  assert.match(result.lines.join('\n'), /1 failure\(s\) stand:/);
});

test('the runner takes its exit code from judgeRun for every run, a passing one included', () => {
  const runner = readFileSync(join(repoRoot, 'scripts', 'e2e.mjs'), 'utf8');
  assert.match(runner, /exitCode = judged\(first, canBeJudged\(passthrough\)\);/);
  // The shape that let a second run's good result stand for the first run's failing status.
  assert.doesNotMatch(runner, /exitCode = first\.status;/);
});
