// SPDX-License-Identifier: AGPL-3.0-or-later
//
// What the failures of a browser run mean, read from the report Playwright writes about it.
//
// A browser cuts off every request it has in flight when the machine's network addresses change
// under it, and reports each as net::ERR_NETWORK_CHANGED. A container starting or stopping
// anywhere on the host is such a change: the interface that joins it to the host gains or loses an
// address. The development server hands a page its code as several hundred separate files, so a
// page that is loading at that moment loses some of them and never starts; the test waits out its
// time on an empty screen and fails, with nothing wrong in the application or in the test. On a
// machine where other work starts containers all day that is what a whole run's failures were:
// measured over the desktop project, ten tests of a hundred and sixty-six failed, the trace of
// every one of the ten showed requests cut off this way, the three that fell inside a recording
// of the host's addresses each fell on the second one changed, and all ten passed when run again.
//
// The rule here is the one that was being applied by hand. A failure is set aside only when the
// browser itself said the network changed during that very test — the guard every spec runs under
// marks the test when it sees a request cut off that way — and being set aside means being run
// again on its own, not being forgiven: its second result is its verdict. A failure with no such
// mark is a failure and is never run again. That is the whole difference from telling Playwright
// to retry, which runs every failure again and reports a test that fails one time in three as
// passed.

import { existsSync, readFileSync } from 'node:fs';

/**
 * The mark the guard leaves on a test during which the browser reported a request cut off by a
 * network change. The guard in client/e2e/consoleGuard.ts writes this same word, and a script test
 * holds the two together.
 */
export const NETWORK_CHANGED = 'network-changed';

/** How many times a failure that was cut off is run again before it is called unjudged. */
export const ATTEMPTS = 2;

/** The report at `path`, or null when the run did not get as far as writing one. */
export function readReport(path) {
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, 'utf8'));
  } catch {
    return null;
  }
}

/**
 * Every test of a report as one flat list.
 *
 * `failed` is Playwright's own word on the test ("unexpected"), so a test expected to fail that
 * failed is not in it and a test that timed out is. `cutOff` is the guard's mark on the attempt
 * that decided that.
 */
export function outcomesOf(report) {
  const outcomes = [];
  const walk = (suite, path) => {
    for (const spec of suite.specs ?? []) {
      for (const test of spec.tests ?? []) {
        const decisive = test.results?.at(-1);
        const marks = [...(decisive?.annotations ?? []), ...(test.annotations ?? [])];
        outcomes.push({
          project: test.projectName ?? '',
          file: spec.file,
          line: spec.line,
          title: [...path, spec.title].join(' › '),
          failed: test.status === 'unexpected',
          cutOff: marks.some((mark) => mark.type === NETWORK_CHANGED),
        });
      }
    }
    for (const child of suite.suites ?? []) {
      walk(child, [...path, child.title]);
    }
  };
  // The outermost suites are the spec files themselves; their titles are file names, which each
  // test already carries.
  for (const fileSuite of report?.suites ?? []) {
    walk(fileSuite, []);
  }
  return outcomes;
}

const keyOf = (outcome) => [outcome.project, outcome.file, outcome.line, outcome.title].join('\n');

/** One line naming a test the way Playwright's own list does. */
export const describe = (outcome) =>
  `[${outcome.project}] ${outcome.file}:${outcome.line} › ${outcome.title}`;

/**
 * The failures of a run, sorted into the ones that stand and the ones to run again.
 *
 * `outside` counts what the run reported outside any test — a worker that died, a project that
 * could not be loaded. Those are never anybody's network.
 */
export function sortFailures(report) {
  const failed = outcomesOf(report).filter((outcome) => outcome.failed);
  return {
    standing: failed.filter((outcome) => !outcome.cutOff),
    cutOff: failed.filter((outcome) => outcome.cutOff),
    outside: report?.errors?.length ?? 0,
  };
}

/**
 * What a second run says about the failures it was started for.
 *
 * A test that was asked for and is not in the report was not judged, and is kept as cut off: a run
 * that did not happen proves nothing about it. Anything else the second run happened to execute —
 * two tests can share a line — is none of its business and is ignored.
 */
export function sortSecondRun(pending, report) {
  const seen = new Map(outcomesOf(report).map((outcome) => [keyOf(outcome), outcome]));
  const passed = [];
  const standing = [];
  const cutOff = [];
  for (const test of pending) {
    const now = seen.get(keyOf(test));
    if (!now) cutOff.push(test);
    else if (!now.failed) passed.push(test);
    else if (now.cutOff) cutOff.push(test);
    else standing.push(test);
  }
  return { passed, standing, cutOff };
}

/**
 * What to hand Playwright to run exactly these tests: per project, the places they are written.
 *
 * The leading slash is what keeps `mobile.spec.ts` from also selecting `scene3d-mobile.spec.ts` —
 * Playwright reads the name as a pattern and looks for it anywhere in the path.
 */
export function selectorsByProject(outcomes) {
  const byProject = new Map();
  for (const outcome of outcomes) {
    const places = byProject.get(outcome.project) ?? new Set();
    places.add(`/${outcome.file}:${outcome.line}`);
    byProject.set(outcome.project, places);
  }
  return new Map([...byProject].map(([project, places]) => [project, [...places]]));
}

/**
 * The arguments of a run with the report added to whatever the caller asked to see.
 *
 * Naming a reporter on the command line replaces the configured ones rather than adding to them,
 * so the report has to be named beside it; and with none named, what Playwright would have chosen
 * by itself is named so that asking for the report does not change what the run prints.
 */
export function withReport(argv, onCi = Boolean(process.env.CI)) {
  const alsoJson = (value) => (value.split(',').includes('json') ? value : `${value},json`);
  const out = [];
  let named = false;
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument.startsWith('--reporter=')) {
      named = true;
      out.push(`--reporter=${alsoJson(argument.slice('--reporter='.length))}`);
    } else if (argument === '--reporter' && index + 1 < argv.length) {
      named = true;
      index += 1;
      out.push(`--reporter=${alsoJson(argv[index])}`);
    } else {
      out.push(argument);
    }
  }
  if (!named) out.push(`--reporter=${onCi ? 'dot' : 'list'},json`);
  return out;
}

/**
 * True when a run is of a kind whose failures can be run again and mean the same thing.
 *
 * Not one a person is steering; not one asked to repeat each test, where a single test is several
 * results and "the one that failed" stops being a place in a file; and not one told to stop at its
 * first failures. That last one matters most: such a run has not executed the tests after the point
 * it stopped at, so a cut-off failure that passes the second time would turn a run of a tenth of
 * the suite green.
 */
export function canBeJudged(argv) {
  return !argv.some((argument) =>
    /^(-x|--(ui|debug|repeat-each|list|max-failures))(=|$)/.test(argument));
}

/** The last word on a run: its exit code, and what to say about it. */
export function verdict({ excused, standing, unjudged, outside }) {
  const lines = [];
  const list = (tests) => tests.map((test) => `    ${describe(test)}`);
  if (excused.length > 0) {
    lines.push(
      `${excused.length} failure(s) were cut off by the network changing under the browser, and `
        + 'passed when run again on their own:',
      ...list(excused),
    );
  }
  if (unjudged.length > 0) {
    lines.push(
      `${unjudged.length} test(s) could not be judged: the network changed in every one of their `
        + `${ATTEMPTS + 1} attempts. Run them again when fewer containers are starting:`,
      ...list(unjudged),
    );
  }
  if (standing.length > 0) {
    lines.push(`${standing.length} failure(s) stand:`, ...list(standing));
  }
  if (outside > 0) {
    lines.push(`The run also reported ${outside} error(s) outside any test; see its output above.`);
  }
  const failed = standing.length > 0 || unjudged.length > 0 || outside > 0;
  return { exitCode: failed ? 1 : 0, lines };
}
