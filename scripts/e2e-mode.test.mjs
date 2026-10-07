// SPDX-License-Identifier: AGPL-3.0-or-later
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';

import {
  FAST_PROJECT,
  LEFT_OUT_OF_FAST,
  describeFast,
  fastRun,
  modeOf,
  namedSpecs,
  phoneOnlyPatterns,
  specsIn,
} from './e2e-mode.mjs';

const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..', 'client');
const specs = specsIn(join(clientDir, 'e2e'));
const phoneOnly = phoneOnlyPatterns(readFileSync(join(clientDir, 'playwright.config.ts'), 'utf8'));
const isPhoneOnly = (name) => phoneOnly.some((pattern) => pattern.test(name));

test('the mode is read from the command line or the environment, and is precise when neither says', () => {
  assert.deepEqual(modeOf(['smoke'], {}), { mode: 'precise', rest: ['smoke'] });
  assert.deepEqual(modeOf(['--mode', 'fast', 'smoke'], {}), { mode: 'fast', rest: ['smoke'] });
  assert.deepEqual(modeOf(['smoke', '--mode=fast'], {}), { mode: 'fast', rest: ['smoke'] });
  assert.deepEqual(modeOf(['smoke'], { SILEXGIS_TEST_MODE: 'Fast' }), { mode: 'fast', rest: ['smoke'] });
  // An empty variable is a variable nobody set.
  assert.equal(modeOf([], { SILEXGIS_TEST_MODE: '' }).mode, 'precise');
  // Said twice and alike is said once.
  assert.equal(modeOf(['--mode', 'fast'], { SILEXGIS_TEST_MODE: 'fast' }).mode, 'fast');
});

test('a mode said two ways, or a mode there is not, starts nothing', () => {
  assert.throws(() => modeOf(['--mode', 'precise'], { SILEXGIS_TEST_MODE: 'fast' }), /two ways/);
  assert.throws(() => modeOf(['--mode', 'quick'], {}), /fast or precise/);
  assert.throws(() => modeOf(['--mode'], {}), /fast or precise/);
  assert.throws(() => modeOf([], { SILEXGIS_TEST_MODE: 'smoke' }), /fast or precise/);
});

test('an option\'s value is not taken for a spec', () => {
  assert.deepEqual(namedSpecs(['smoke', '--workers', '4', '-g', 'signs in', 'trips.spec.ts:40']), [
    'smoke',
    'trips.spec.ts:40',
  ]);
  assert.deepEqual(namedSpecs(['--project=desktop', '--headed', '--repeat-each', '3']), []);
});

test('with nothing named, the fast form is the desktop project without the 3D scenes', () => {
  const { argv, leftOut, refusal } = fastRun(['--workers=8'], specs, phoneOnly);
  assert.equal(refusal, undefined);
  assert.deepEqual(argv.slice(0, 2), ['--workers=8', `--project=${FAST_PROJECT}`]);
  // The tests of a file side by side: the part of the fast form that buys the most.
  assert.equal(argv.at(-1), '--fully-parallel');

  const running = argv.slice(2, -1).map((path) => path.replace(/^e2e\//, ''));
  // Every desktop spec that is not on the list, and nothing else: no phone spec, no 3D scene,
  // and no spec dropped by accident.
  assert.deepEqual(
    running,
    specs.filter((name) => !isPhoneOnly(name) && !LEFT_OUT_OF_FAST.includes(name)),
  );
  assert.ok(running.length > 40, 'the fast form still runs most of the suite');
  assert.deepEqual(leftOut, { phoneProjects: true, specs: LEFT_OUT_OF_FAST });
});

test('every spec the fast form leaves out exists, and none of them is a phone spec', () => {
  // A name that matches nothing would be a spec quietly running again in the fast form, and a
  // phone spec here would be left out twice and counted once.
  for (const name of LEFT_OUT_OF_FAST) {
    assert.ok(specs.includes(name), `${name} is not a spec on disk`);
    assert.equal(isPhoneOnly(name), false, `${name} is a phone spec`);
  }
});

test('a spec in the fast form is selected by a path that selects no other', () => {
  // `trips.spec.ts` is also the end of `my-trips.spec.ts`; the path handed to Playwright is a
  // pattern, so each one has to carry enough of the path to be one file's.
  const { argv } = fastRun([], specs, phoneOnly);
  const paths = argv.filter((a) => a.startsWith('e2e/'));
  for (const path of paths) {
    const selected = specs.filter((name) => new RegExp(path).test(`/checkout/client/e2e/${name}`));
    assert.deepEqual(selected, [path.slice('e2e/'.length)], `${path} selects ${selected.join(', ')}`);
  }
});

test('a spec asked for by name runs, even one the fast form would have left out', () => {
  const asked = fastRun(['scene3d.spec.ts'], specs, phoneOnly);
  assert.deepEqual(asked.argv, ['scene3d.spec.ts', `--project=${FAST_PROJECT}`, '--fully-parallel']);
  assert.deepEqual(asked.leftOut, { phoneProjects: true, specs: [] });
});

test('a phone spec asked for by name is refused before anything is started', () => {
  const { refusal } = fastRun(['mobile-ios'], specs, phoneOnly);
  assert.match(refusal, /belongs to a phone project/);
  // Named together with its project, it is simply the request.
  const withProject = fastRun(['mobile-ios', '--project=mobile-ios-smoke'], specs, phoneOnly);
  assert.equal(withProject.refusal, undefined);
  assert.deepEqual(withProject.argv, ['mobile-ios', '--project=mobile-ios-smoke', '--fully-parallel']);
  assert.deepEqual(withProject.leftOut, { phoneProjects: false, specs: [] });
});

test('a project named without a spec keeps its own meaning', () => {
  // The desktop project: still the fast form's list, and the project is not named twice.
  const desktop = fastRun(['--project=desktop'], specs, phoneOnly);
  assert.equal(desktop.argv.filter((a) => a.startsWith('--project')).length, 1);
  assert.deepEqual(desktop.leftOut.specs, LEFT_OUT_OF_FAST);
  // A phone project: the request as it stands.
  const phone = fastRun(['--project', 'mobile-android'], specs, phoneOnly);
  assert.deepEqual(phone.argv, ['--project', 'mobile-android', '--fully-parallel']);
  // Asked for twice is asked for once.
  assert.equal(
    fastRun(['--fully-parallel', 'smoke'], specs, phoneOnly).argv.filter((a) => a === '--fully-parallel').length,
    1,
  );
  assert.deepEqual(phone.leftOut, { phoneProjects: false, specs: [] });
});

test('a fast run says what it did not run', () => {
  assert.match(
    describeFast({ phoneProjects: true, specs: ['scene3d.spec.ts', 'terrain.spec.ts'] }),
    /NOT the whole browser suite: the tests of a file ran side by side, and left out the three phone projects and 2 specs that draw a 3D scene \(scene3d\.spec\.ts, terrain\.spec\.ts\)/,
  );
  assert.match(describeFast({ phoneProjects: true, specs: [] }), /left out the three phone projects$/);
  assert.match(describeFast({ phoneProjects: false, specs: [] }), /nothing was left out/);
});
