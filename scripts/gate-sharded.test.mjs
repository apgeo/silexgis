// SPDX-License-Identifier: AGPL-3.0-or-later
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { listClasses, shard, testDir } from './gate-shard.mjs';
import { summarise } from './gate-lock.mjs';
import { parseArguments, quietClasses, summaryLineOf, threadsFor } from './gate-sharded.mjs';

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
});

test('arguments are read by name, and one it does not know is refused', () => {
  assert.deepEqual(parseArguments([]), {
    count: 8,
    threads: null,
    results: null,
    project: null,
    only: null,
  });
  assert.deepEqual(parseArguments(['--count', '6', '--threads', '5', '--results', 'out']), {
    count: 6,
    threads: 5,
    results: 'out',
    project: null,
    only: null,
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
