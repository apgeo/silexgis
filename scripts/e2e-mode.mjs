// SPDX-License-Identifier: AGPL-3.0-or-later

// The fast form of the browser suite: what `node scripts/e2e.mjs --mode fast` leaves out.
//
// Measured over a whole run (2026-10-07, 220 tests): the tests' own time adds up to two hours of
// worker time, and what sets the wall clock is not the number of workers but the longest FILE —
// Playwright runs the tests of a file one after another in one worker, and two files hold a
// fifth of the suite between them. The phone projects are an eighth of the time and the specs
// that draw a 3D scene a twelfth. So the fast form does three things, in order of what they buy:
//
//   - the tests of a file are spread over the workers (`--fully-parallel`) instead of taking
//     turns in one. This is the browser suite's version of what the integration suite's fast
//     mode gives up: two tests of a file that work on the same record of the demonstration data
//     now may do so at the same moment. A file that says it must run in order still does.
//   - the three phone projects are left out. They regress the phone layout, and a change to a
//     desktop page is proved on the desktop project first.
//   - the specs that draw a 3D scene are left out. There is no graphics card here, every scene is
//     rendered in software, and several at once starve each other.
//
// It is for the look taken while a piece of work is in progress. A pass in this form is not the
// browser suite's verdict, and the runner's last line says what did not run. A failure seen only
// in this form is run again in the ordinary one before it is believed, exactly as for the
// integration suite. Naming spec files narrows the run further and is honoured as given: a spec
// asked for by name runs even when the fast form would have left it out, because whoever names
// it is asking about it.
//
// Pure functions, so the rule can be tested without a browser.

import { readFileSync, readdirSync } from 'node:fs';

/** The one project of the fast form. */
export const FAST_PROJECT = 'desktop';

/** Playwright's own switch for running the tests of a file side by side. */
const SPREAD = '--fully-parallel';
const spread = (argv) => (argv.includes(SPREAD) ? argv : [...argv, SPREAD]);

/**
 * The specs whose subject is a 3D scene, by file name. Kept as names rather than measured at run
 * time: what makes them slow is what they are, not how busy the machine was.
 */
export const LEFT_OUT_OF_FAST = [
  'scene3d.spec.ts',
  'scene3d-walls.spec.ts',
  'terrain.spec.ts',
  'terrain-derivatives.spec.ts',
  'tracking-movie.spec.ts',
];

/** Playwright options that take their value as the next argument; the value is not a spec. */
const TAKES_A_VALUE = new Set([
  '-c', '--config', '-g', '--grep', '--grep-invert', '--global-timeout', '-j', '--workers',
  '--max-failures', '--output', '--project', '--repeat-each', '--reporter', '--retries',
  '--shard', '--timeout', '--trace', '--tsconfig', '--only-changed', '--ui-host', '--ui-port',
]);

/**
 * The mode a run is in and the arguments left for Playwright: `--mode fast|precise` (or
 * `--mode=…`) on the command line, else SILEXGIS_TEST_MODE, else precise.
 *
 * Told two different things, or a mode there is not, it refuses — the same rule the integration
 * runners keep, for the same reason: a run labelled one way and executed the other is the mistake
 * nothing downstream could notice.
 */
export function modeOf(argv, environment = process.env) {
  const rest = [];
  let asked = null;
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === '--mode') {
      asked = argv[index + 1] ?? '';
      index += 1;
    } else if (argument.startsWith('--mode=')) {
      asked = argument.slice('--mode='.length);
    } else {
      rest.push(argument);
    }
  }
  const said = [['--mode', asked], ['SILEXGIS_TEST_MODE', environment.SILEXGIS_TEST_MODE ?? null]]
    .filter(([, value]) => value !== null)
    .map(([where, value]) => [where, value.trim().toLowerCase()])
    .filter(([where, value]) => value !== '' || where === '--mode');
  for (const [where, value] of said) {
    if (value !== 'fast' && value !== 'precise') {
      throw new Error(`${where} is fast or precise, not "${value}"`);
    }
  }
  if (new Set(said.map(([, value]) => value)).size > 1) {
    throw new Error(
      `the test mode is said two ways — ${said.map(([where, value]) => `${where} says ${value}`).join(', ')} — say one`);
  }
  return { mode: said[0]?.[1] ?? 'precise', rest };
}

/** The arguments that name spec files or lines, as opposed to options and their values. */
export function namedSpecs(argv) {
  const named = [];
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (TAKES_A_VALUE.has(argument)) {
      index += 1;
    } else if (!argument.startsWith('-')) {
      named.push(argument);
    }
  }
  return named;
}

/** The regex literals of the Playwright config's phone-only list, read from its text. */
export function phoneOnlyPatterns(configText) {
  const block = /const PHONE_ONLY_SPECS = \[([\s\S]*?)\];/.exec(configText);
  if (!block) {
    throw new Error('playwright.config.ts no longer declares PHONE_ONLY_SPECS as an array literal');
  }
  return [...block[1].matchAll(/\/((?:[^/\\\n]|\\.)+)\/[gimsuy]*/g)].map((m) => new RegExp(m[1]));
}

/** Every `*.spec.ts` in the suite directory, as bare file names. */
export function specsIn(directory) {
  return readdirSync(directory).filter((name) => name.endsWith('.spec.ts')).sort();
}

/**
 * What the fast form runs, given what the caller asked for.
 *
 * Returns the arguments for Playwright and, for the run's own account of itself, what was left
 * out. `refusal` is set instead when the request cannot be met in this form: every spec that was
 * named belongs to a phone project, and a run that would find no test is better refused before a
 * database has been started for it.
 */
export function fastRun(argv, specs, phoneOnly) {
  const isPhoneOnly = (name) => phoneOnly.some((pattern) => pattern.test(name));
  const ownProject = argv.some((a) => a === '--project' || a.startsWith('--project='));
  const named = namedSpecs(argv);

  if (named.length > 0) {
    // Named specs are run as named. Only the project is still the fast form's, unless one was
    // named too — and then nothing of the fast form is left to apply, which is said.
    const matched = specs.filter((name) => named.some((filter) => matches(filter, `e2e/${name}`)));
    if (!ownProject && matched.length > 0 && matched.every(isPhoneOnly)) {
      return {
        refusal:
          `the fast form runs the ${FAST_PROJECT} project only, and ${matched.join(', ')} `
            + `${matched.length === 1 ? 'belongs' : 'belong'} to a phone project: `
            + 'run without --mode fast, or name the project with --project',
      };
    }
    return {
      argv: spread(ownProject ? argv : [...argv, `--project=${FAST_PROJECT}`]),
      leftOut: { phoneProjects: !ownProject, specs: [] },
    };
  }

  if (ownProject && projectsNamed(argv).some((name) => name !== FAST_PROJECT)) {
    // A phone project was asked for by name: that is the request, and the fast form has nothing
    // to take from it — its list of specs is the desktop project's.
    return { argv: spread(argv), leftOut: { phoneProjects: false, specs: [] } };
  }

  const leftOut = LEFT_OUT_OF_FAST.filter((name) => specs.includes(name));
  const running = specs.filter((name) => !isPhoneOnly(name) && !leftOut.includes(name));
  return {
    argv: spread([
      ...argv,
      ...(ownProject ? [] : [`--project=${FAST_PROJECT}`]),
      ...running.map((name) => `e2e/${name}`),
    ]),
    leftOut: { phoneProjects: true, specs: leftOut },
  };
}

/** The projects a command line names with `--project`. */
function projectsNamed(argv) {
  const names = [];
  for (let index = 0; index < argv.length; index += 1) {
    if (argv[index] === '--project' && index + 1 < argv.length) names.push(argv[index + 1]);
    else if (argv[index].startsWith('--project=')) names.push(argv[index].slice('--project='.length));
  }
  return names;
}

/** Whether a spec filter, read the way Playwright reads one, selects a path. */
function matches(filter, path) {
  // A filter may end in `:line` or `:line:column`; what precedes it is a regular expression.
  const pattern = filter.replace(/:\d+(:\d+)?$/, '');
  try {
    return new RegExp(pattern).test(path);
  } catch {
    return path.includes(pattern);
  }
}

/** One line saying what a fast run does not cover, for the start and the end of its output. */
export function describeFast(leftOut) {
  const parts = [];
  if (leftOut.phoneProjects) parts.push('the three phone projects');
  if (leftOut.specs.length > 0) {
    parts.push(`${leftOut.specs.length} specs that draw a 3D scene (${leftOut.specs.join(', ')})`);
  }
  const together = 'the tests of a file ran side by side';
  return parts.length === 0
    ? `test mode: fast — ${together}; nothing was left out, the request named its own project`
    : `test mode: fast — NOT the whole browser suite: ${together}, and left out ${parts.join(' and ')}`;
}

/** Reads what `fastRun` needs from a checkout. */
export function suiteOf(clientDir, join) {
  return {
    specs: specsIn(join(clientDir, 'e2e')),
    phoneOnly: phoneOnlyPatterns(readFileSync(join(clientDir, 'playwright.config.ts'), 'utf8')),
  };
}
