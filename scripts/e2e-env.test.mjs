// SPDX-License-Identifier: AGPL-3.0-or-later
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { join } from 'node:path';

import { forTheApi, leftoverApiPid, playwrightCommand, processStartTime } from './e2e-env.mjs';

test('an inherited database, file store or listening address never reaches the e2e API', () => {
  const inherited = {
    PATH: '/usr/bin',
    SILEXGIS__Db__ConnectionString: 'Host=elsewhere;Database=shared',
    Db__ConnectionString: 'Host=elsewhere;Database=unprefixed',
    SILEXGIS__Files__Root: '/somebody/else/files',
    SILEXGIS__Keys__Path: '/somebody/else/keys',
    SILEXGIS__Terrain__PublishRoot: '/somebody/else/terrain',
    SILEXGIS__Terrain__BuildRoot: '/somebody/else/builds',
    ASPNETCORE_URLS: 'http://127.0.0.1:5080',
    // A behaviour switch is a deliberate choice of the caller and is kept: a spec documents being
    // run with one, and the photo-library wrapper hands its libraries over this way.
    SILEXGIS__TripTracking__PublishDepthPlaces: 'false',
    SILEXGIS__PhotoLibraries__Immich__Enabled: 'true',
    SILEXGIS_E2E_API_PORT: '6112',
  };
  const env = forTheApi(inherited, { SILEXGIS__Db__ConnectionString: 'Host=localhost;Port=6102;Database=silexgis' });

  assert.equal(env.SILEXGIS__Db__ConnectionString, 'Host=localhost;Port=6102;Database=silexgis');
  for (const key of ['Db__ConnectionString', 'SILEXGIS__Files__Root', 'SILEXGIS__Keys__Path',
    'SILEXGIS__Terrain__PublishRoot', 'SILEXGIS__Terrain__BuildRoot', 'ASPNETCORE_URLS']) {
    assert.equal(key in env, false, `${key} was inherited`);
  }
  assert.equal(env.SILEXGIS__TripTracking__PublishDepthPlaces, 'false');
  assert.equal(env.SILEXGIS__PhotoLibraries__Immich__Enabled, 'true');
  assert.equal(env.SILEXGIS_E2E_API_PORT, '6112');
  assert.equal(env.PATH, '/usr/bin');
});

test('Playwright is started without a shell, so a free-text filter reaches it as one argument', () => {
  const run = playwrightCommand(join('repo', 'client'), ['-g', 'a link is anchored', '-g', 'a|b'], '/usr/bin/node');
  assert.equal(run.command, '/usr/bin/node');
  assert.deepEqual(run.args, [
    join('repo', 'client', 'node_modules', '@playwright', 'test', 'cli.js'),
    'test', '-g', 'a link is anchored', '-g', 'a|b',
  ]);
});

test('a pid left by an earlier run is stopped only while it still names the process that wrote it', () => {
  const startOf = (pid) => (pid === 4242 ? '777' : null);
  assert.equal(leftoverApiPid('4242 777', startOf), 4242);
  // The same number, a different process — as after a reboot.
  assert.equal(leftoverApiPid('4242 123', startOf), null);
  // No start time recorded, or none readable: never a kill.
  assert.equal(leftoverApiPid('4242', startOf), null);
  assert.equal(leftoverApiPid('5151 777', startOf), null);
  assert.equal(leftoverApiPid('garbage', startOf), null);
});

test('a start time is read for a live process where the platform has one', { skip: process.platform !== 'linux' }, () => {
  assert.match(processStartTime(process.pid) ?? '', /^\d+$/);
});
