// SPDX-License-Identifier: AGPL-3.0-or-later
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { listClasses, shard, testDir } from './gate-shard.mjs';
import { summarise } from './gate-lock.mjs';
import {
  hostsOf,
  modeComplaint,
  modeOf,
  parseArguments,
  quietClasses,
  sharesFor,
  summaryLineOf,
  threadsFor,
  whereApplicationsWereBuilt,
} from './gate-sharded.mjs';

const green = 'Passed!  - Failed:     0, Passed:   310, Skipped:     0, Total:   310, Duration: 41 m 2 s - SilexGis.Api.Tests.dll (net10.0)';
const red = 'Failed!  - Failed:     2, Passed:   301, Skipped:     1, Total:   304, Duration: 39 m - SilexGis.Api.Tests.dll (net10.0)';

test('a shard is summed up by the last summary line its runner printed', () => {
  const output = `Starting test execution, please wait...\n  Failed SomeTests.A_test [1 s]\n${red}\n`;
  assert.equal(summaryLineOf(output), red);
  // A run that printed two — it does when a logger repeats it — is the later one.
  assert.equal(summaryLineOf(`${red}\nmore output\n${green}\n`), green);
});

test('a shard whose runner never reached a summary has none, rather than an empty one', () => {
  assert.equal(summaryLineOf('Starting test execution, please wait...\nThe active test run was aborted.'), null);
  assert.equal(summaryLineOf(''), null);
});

test('the shards\' summary lines add up to one verdict for whoever reads the output', () => {
  // The lock's reader is that somebody: it must count every shard and call one red shard red.
  const printed = [summaryLineOf(green), summaryLineOf(red)].join('\n');
  const verdict = summarise(printed);

  assert.equal(verdict.assemblies, 2);
  assert.equal(verdict.total, 614);
  assert.equal(verdict.failed, 2);
  assert.equal(verdict.verdict, 'red');
  assert.equal(summarise([green, green].join('\n')).verdict, 'green');
});

test('the cores are shared out between the shards, and no shard is left with one thread', () => {
  assert.equal(threadsFor(8, 32), 4);
  assert.equal(threadsFor(12, 32), 2);
  assert.equal(threadsFor(8, 4), 2);
  // A lone shard is not handed every core: past eight threads a process gains nothing.
  assert.equal(threadsFor(1, 32), 8);
});

test('arguments are read by name, and one it does not know is refused', () => {
  assert.deepEqual(parseArguments([]), {
    count: 8,
    threads: null,
    results: null,
    project: null,
    only: null,
    mode: null,
    classes: null,
    affected: null,
  });
  assert.deepEqual(parseArguments(['--count', '6', '--threads', '5', '--results', 'out']), {
    count: 6,
    threads: 5,
    results: 'out',
    project: null,
    only: null,
    mode: null,
    classes: null,
    affected: null,
  });
  assert.throws(() => parseArguments(['--count', '0']), /whole number/);
  assert.throws(() => parseArguments(['--count']), /incomplete/);
  assert.throws(() => parseArguments(['--shards', '8']), /unknown/);
});

test('some shards of a deal can be run without the rest, and only ones the deal has', () => {
  assert.deepEqual(parseArguments(['--count', '8', '--only', '2,5']).only, [2, 5]);
  // Whichever order the two are given in, the deal is what the numbers are checked against.
  assert.deepEqual(parseArguments(['--only', '7', '--count', '8']).only, [7]);
  assert.throws(() => parseArguments(['--count', '8', '--only', '8']), /does not have/);
  assert.throws(() => parseArguments(['--only', 'two']), /shard numbers/);
  assert.throws(() => parseArguments(['--only', '-1']), /shard numbers/);
  // The classes that run alone are asked for by name, beside any shard or without one.
  assert.deepEqual(parseArguments(['--count', '8', '--only', '2,quiet']).only, [2, 'quiet']);
  assert.deepEqual(parseArguments(['--only', 'quiet']).only, ['quiet']);
});

test('a class that measures responsiveness is kept out of the deal and still runs', () => {
  const all = listClasses();
  const quiet = quietClasses(testDir, all);

  // The suite has such a class today; were the collection ever emptied this would say so, which
  // is the moment to ask whether the phase is still needed.
  assert.ok(quiet.includes('PerformanceTests'), 'PerformanceTests is no longer in the serial collection');

  const dealt = all.filter((name) => !quiet.includes(name));
  const inShards = Array.from({ length: 8 }, (_, index) => shard(dealt, index, 8)).flat();
  assert.equal(quiet.some((name) => inShards.includes(name)), false);
  // Nothing is lost between the two: every class is in a shard or in the quiet run, once.
  assert.deepEqual([...inShards, ...quiet].sort(), [...all].sort());
});

test('the mode is what the command says, else what the environment says, else precise', () => {
  assert.equal(modeOf({ mode: null }, {}), 'precise');
  assert.equal(modeOf({ mode: 'fast' }, {}), 'fast');
  assert.equal(modeOf({ mode: null }, { SILEXGIS_TEST_MODE: 'fast' }), 'fast');
  assert.equal(modeOf({ mode: null }, { SILEXGIS_TEST_MODE: ' Precise ' }), 'precise');
  assert.equal(modeOf({ mode: 'fast' }, { SILEXGIS_TEST_MODE: 'fast' }), 'fast');
  assert.equal(parseArguments(['--mode', 'fast']).mode, 'fast');
});

test('a run told two different modes, or one nobody defined, does not start', () => {
  assert.throws(() => modeOf({ mode: 'fast' }, { SILEXGIS_TEST_MODE: 'precise' }), /say one of them/);
  assert.throws(() => modeOf({ mode: null }, { SILEXGIS_TEST_MODE: 'quick' }), /fast or precise/);
  assert.throws(() => parseArguments(['--mode', 'quick']), /fast or precise/);
});

test('what the test processes say they did is added up, and a silent one is counted as silent', () => {
  assert.deepEqual(
    hostsOf([
      { mode: 'fast', built: 40, borrowed: 260 },
      { mode: 'fast', built: 35, borrowed: 301 },
      null,
    ]),
    { processes: 2, missing: 1, built: 75, borrowed: 561, modes: ['fast'], because: {}, classes: {} },
  );
  assert.equal(hostsOf([null, null]), null);
});

test('why applications were built is added up too, by reason and by class', () => {
  const hosts = hostsOf([
    {
      mode: 'fast', built: 5, borrowed: 20,
      because: { 'the first to ask for it': 2, 'made inside a test': 3 },
      classes: {
        ATests: { built: 4, borrowed: 12, because: { 'the first to ask for it': 1, 'made inside a test': 3 } },
        BTests: { built: 1, borrowed: 8, because: { 'the first to ask for it': 1 } },
      },
    },
    {
      mode: 'fast', built: 9, borrowed: 1,
      because: { 'made inside a test': 2, 'given services of the test\'s own': 7 },
      classes: {
        CTests: { built: 7, borrowed: 0, because: { 'given services of the test\'s own': 7 } },
        ATests: { built: 2, borrowed: 1, because: { 'made inside a test': 2 } },
      },
    },
  ]);
  assert.deepEqual(hosts.because, {
    'the first to ask for it': 2,
    'made inside a test': 5,
    'given services of the test\'s own': 7,
  });
  assert.deepEqual(hosts.classes.ATests, {
    built: 6, borrowed: 13, because: { 'the first to ask for it': 1, 'made inside a test': 5 },
  });

  // The reasons first, most frequent first; then the classes that built more than the one a
  // class cannot do without, most first.
  assert.deepEqual(whereApplicationsWereBuilt(hosts), [
    "      7  given services of the test's own",
    '      5  made inside a test',
    '      2  the first to ask for it',
    "      7  built by CTests (handed one 0 times): 7 given services of the test's own",
    '      6  built by ATests (handed one 13 times): 5 made inside a test; 1 the first to ask for it',
  ]);
  assert.equal(whereApplicationsWereBuilt(hosts, 1).length, 4);
});

test('a run of some classes is dealt into no more shards than they are worth', () => {
  // The whole suite: as many as asked.
  assert.equal(sharesFor(8, 262, { restricted: false, mode: 'precise' }), 8);
  assert.equal(sharesFor(8, 262, { restricted: false, mode: 'fast' }), 8);
  // Some classes, precise: a class is minutes, so a process for every two.
  assert.equal(sharesFor(8, 3, { restricted: true, mode: 'precise' }), 2);
  assert.equal(sharesFor(8, 40, { restricted: true, mode: 'precise' }), 8);
  // Some classes, fast: a class is seconds, so half a dozen to a database server.
  assert.equal(sharesFor(8, 3, { restricted: true, mode: 'fast' }), 1);
  assert.equal(sharesFor(8, 14, { restricted: true, mode: 'fast' }), 3);
  assert.equal(sharesFor(8, 200, { restricted: true, mode: 'fast' }), 8);
  // Never none, and never more than was asked for.
  assert.equal(sharesFor(8, 0, { restricted: true, mode: 'fast' }), 1);
  assert.equal(sharesFor(2, 40, { restricted: true, mode: 'precise' }), 2);
});

test('a run that did not do what its mode says is refused, whichever way it went wrong', () => {
  const fast = (borrowed) => ({ processes: 8, missing: 0, built: 300, borrowed, modes: ['fast'] });
  assert.equal(modeComplaint('fast', fast(2000)), null);
  // Asked to share and shared nothing: a slow run wearing the quick one's label.
  assert.match(modeComplaint('fast', fast(0)), /no test was handed a running application/);
  // Of a few classes that may be honest — one test each — so only the whole suite is held to it.
  assert.equal(modeComplaint('fast', fast(0), { whole: false }), null);
  assert.match(
    modeComplaint('precise', { processes: 1, missing: 0, built: 9, borrowed: 2, modes: ['precise'] }, { whole: false }),
    /2 tests were handed/,
  );
  // Asked for a test's own application each time and some were handed another's.
  assert.match(
    modeComplaint('precise', { processes: 8, missing: 0, built: 2600, borrowed: 3, modes: ['precise'] }),
    /3 tests were handed/,
  );
  // A process that ran in the other mode — a stale environment, say.
  assert.match(
    modeComplaint('precise', { processes: 2, missing: 0, built: 10, borrowed: 0, modes: ['precise', 'fast'] }),
    /fast is what some test processes say/,
  );
  assert.equal(modeComplaint('precise', { processes: 8, missing: 0, built: 2600, borrowed: 0, modes: ['precise'] }), null);
  // No account at all proves nothing either way, and is said rather than judged.
  assert.equal(modeComplaint('fast', null), null);
});

test('a run can be of named classes only, and a name that is no class is refused', () => {
  assert.deepEqual(parseArguments(['--classes', 'CalendarTests, TripAndTagTests']).classes, [
    'CalendarTests',
    'TripAndTagTests',
  ]);
  // Not a class name: a filter expression, a path, nothing at all.
  assert.throws(() => parseArguments(['--classes', 'FullyQualifiedName~Calendar']), /class names/);
  assert.throws(() => parseArguments(['--classes', 'tests/CalendarTests.cs']), /class names/);
  assert.throws(() => parseArguments(['--classes', ',']), /class names/);
});

test('a run can be of the classes a change selects, and not of those and named ones at once', () => {
  assert.equal(parseArguments(['--affected', 'master']).affected, 'master');
  assert.throws(() => parseArguments(['--affected']), /unknown or incomplete/);
  assert.throws(() => parseArguments(['--affected', 'master', '--classes', 'CalendarTests']), /say one of them/);
});
