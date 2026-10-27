// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Tests for the cross-repository run of the SpeleoLoc live sync tests.
//
// Run with the rest: `node --test "deploy/**/*.test.mjs"`. Importing the script does not run it,
// so nothing here starts a server or Flutter. What these guard is the verdict: a run that turned
// green on a placeholder, on a selection that matched nothing, or on output cut off half way
// would tell both efforts the API holds when nothing was checked.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, it } from 'node:test';

import {
  LIVE_SWITCH,
  UP_READY_MARKER,
  liveTestFiles,
  renderReport,
  runName,
  summarizeFlutterJson,
  verdictOf,
} from './speleoloc-e2e.mjs';

const ev = (o) => JSON.stringify(o);
const start = (id, name) => ev({ type: 'testStart', test: { id, name, suiteID: 0 } });
const doneEv = (id, result, extra = {}) => ev({ type: 'testDone', testID: id, result, hidden: false, skipped: false, ...extra });

describe('reading flutter test --reporter json', () => {
  it('counts passes and failures and ignores lines that are not JSON', () => {
    const text = [
      'Resolving dependencies...',
      start(1, 'loading test/a_test.dart'), doneEv(1, 'success', { hidden: true }),
      start(2, 'signs in'), doneEv(2, 'success'),
      start(3, 'downloads a set'), ev({ type: 'error', testID: 3, error: 'Expected: 13\n  Actual: 11' }), doneEv(3, 'failure'),
      start(4, 'later'), doneEv(4, 'success', { skipped: true }),
      ev({ type: 'done', success: false }),
    ].join('\n');
    const s = summarizeFlutterJson(text);
    assert.deepEqual([s.passed, s.failed, s.skipped], [1, 1, 1]);
    assert.deepEqual(s.failures, [{ name: 'downloads a set', error: 'Expected: 13' }]);
    assert.equal(s.runnerSuccess, false);
  });

  it('counts a file that failed to load, which the runner reports as a loading test', () => {
    const s = summarizeFlutterJson([start(1, 'loading test/x_test.dart'),
      ev({ type: 'error', testID: 1, error: 'Compilation failed' }), doneEv(1, 'error', { hidden: false })].join('\n'));
    assert.equal(s.failed, 1);
  });

  it('counts a test that started and never finished as failed', () => {
    const s = summarizeFlutterJson(start(1, 'uploads'));
    assert.equal(s.failed, 1);
    assert.equal(s.runnerSuccess, null);
  });

  it('does not count the placeholder a live test registers when it did not see its switch', () => {
    const s = summarizeFlutterJson([start(1, `SilexGIS live test skipped (set ${LIVE_SWITCH}=1)`),
      doneEv(1, 'success'), ev({ type: 'done', success: true })].join('\n'));
    assert.equal(s.passed, 0);
    assert.equal(s.placeholders, 1);
    assert.equal(verdictOf(s), 'red: no live test ran');
  });
});

describe('the verdict', () => {
  it('is green only when something passed and nothing failed', () => {
    assert.equal(verdictOf({ passed: 3, failed: 0 }), 'green');
    assert.equal(verdictOf({ passed: 3, failed: 1 }), 'red');
    assert.equal(verdictOf({ passed: 0, failed: 0 }), 'red: no live test ran');
  });
});

describe('finding the live tests', () => {
  const dir = mkdtempSync(join(tmpdir(), 'speleoloc-e2e-'));
  after(() => rmSync(dir, { recursive: true, force: true }));

  it('takes the test files that read the switch, and only those', () => {
    mkdirSync(join(dir, 'test', 'services', 'silexgis'), { recursive: true });
    writeFileSync(join(dir, 'test', 'services', 'silexgis', 'silexgis_live_test.dart'), `env['${LIVE_SWITCH}']`);
    writeFileSync(join(dir, 'test', 'services', 'silexgis', 'contract_upload_test.dart'), 'replay');
    writeFileSync(join(dir, 'test', 'b_live_test.dart'), `${LIVE_SWITCH}`);
    writeFileSync(join(dir, 'test', 'notes.md'), `${LIVE_SWITCH}`);
    assert.deepEqual(liveTestFiles(dir), ['test/b_live_test.dart', 'test/services/silexgis/silexgis_live_test.dart']);
  });

  it('finds none in a checkout without a test directory', () => {
    assert.deepEqual(liveTestFiles(join(dir, 'nowhere')), []);
  });
});

describe('the report', () => {
  const base = {
    startedAt: new Date('2026-10-28T09:15:42Z'), seconds: 61,
    silexgis: { commit: '4610f41a', dirty: false }, app: { commit: '99a5d23f', dirty: true },
    files: ['test/services/silexgis/silexgis_live_test.dart'], logDir: '/x/logs/run',
  };

  it('is named for the time and the commit pair, sortably', () => {
    assert.equal(runName(base), '2026-10-28T09-15Z-silexgis-4610f41a-speleoloc-99a5d23f');
  });

  it('names both commits, flags uncommitted changes and lists each failure', () => {
    const text = renderReport({ ...base, summary: { passed: 4, failed: 1, skipped: 0, placeholders: 0,
      failures: [{ name: 'uploads a photo', error: 'sync.file_bytes_missing' }] } });
    assert.match(text, /— red$/m);
    assert.match(text, /`4610f41a`\n/);
    assert.match(text, /`99a5d23f` \+ uncommitted changes/);
    assert.match(text, /\| 4 \| 1 \| 0 \| 0 \|/);
    assert.match(text, /\*\*uploads a photo\*\* — sync\.file_bytes_missing/);
  });

  it('says the tests were not run when the server did not come up', () => {
    const text = renderReport({ ...base, summary: null, setupProblem: 'port taken' });
    assert.match(text, /not run: the server did not come up/);
    assert.doesNotMatch(text, /Passed/);
  });
});

describe('waiting for the server', () => {
  it('waits for the line `up` prints only after it has proved the server', async () => {
    const { readFileSync } = await import('node:fs');
    const dev = readFileSync(new URL('./speleoloc-dev.mjs', import.meta.url), 'utf8');
    const marker = dev.indexOf(UP_READY_MARKER);
    assert.ok(marker > 0, 'speleoloc-dev.mjs no longer prints the line this script waits for');
    assert.ok(dev.lastIndexOf('proveSyncContent(baseUrl') < dev.lastIndexOf('printSummary(baseUrl, capabilities)'),
      'the summary must be printed after the proof');
  });
});
