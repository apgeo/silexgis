// SPDX-License-Identifier: AGPL-3.0-or-later
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

import { appPasswordIn, pinnedVersions, requiredImmichPermissions } from './e2e-photo-libraries.mjs';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');

test('the libraries are started at the builds the deployment example names', () => {
  const example = readFileSync(join(repoRoot, 'deploy', '.env.example'), 'utf8');
  const versions = pinnedVersions(example);

  // Whatever they are today, each is a pinned build and not a moving tag.
  assert.match(versions.immich, /^v\d+\.\d+\.\d+$/);
  assert.match(versions.photoprism, /^\d{6}$/);
});

test('a build is read whether its line is commented out or not, and a missing one is said', () => {
  const example = [
    '# SILEXGIS_IMMICH_VERSION=v3.1.0',
    'SILEXGIS_PHOTOPRISM_VERSION=260728',
  ].join('\n');
  assert.deepEqual(pinnedVersions(example), { immich: 'v3.1.0', photoprism: '260728' });

  assert.throws(
    () => pinnedVersions('# SILEXGIS_IMMICH_VERSION=v3.1.0\n'),
    /no longer names SILEXGIS_PHOTOPRISM_VERSION/,
  );
});

test('the key is given the rights the application says it needs, and the deployment files name each', () => {
  const required = requiredImmichPermissions(
    readFileSync(
      join(repoRoot, 'server', 'src', 'SilexGis.Infrastructure', 'PhotoLibraries', 'ImmichClient.cs'),
      'utf8',
    ),
  );
  assert.ok(required.includes('map.read'), 'the list read from the client is not the permission list');

  // The two places an operator reads the list. Both once fell behind the code — one said two
  // rights and the other three when the application had come to need four — and a key made by
  // either drew a map and then failed on the surfaces built since.
  for (const file of ['.env.example', 'docker-compose.immich.yml']) {
    const text = readFileSync(join(repoRoot, 'deploy', file), 'utf8');
    for (const permission of required) {
      assert.ok(text.includes(permission), `deploy/${file} does not name ${permission}`);
    }
  }
});

test('a client that no longer lists its permissions is said, rather than read as needing none', () => {
  assert.throws(() => requiredImmichPermissions('class ImmichClient { }'), /no longer lists/);
});

test('the application password is picked out of what the library printed, and nothing else is', () => {
  const printed = [
    'time="2026-10-06T06:00:00Z" level=info msg="session created"',
    'App Password:  a1B2c3-D4e5F6-g7H8i9-J0k1L2',
    'Expires:       2027-10-06',
  ].join('\n');
  assert.equal(appPasswordIn(printed), 'a1B2c3-D4e5F6-g7H8i9-J0k1L2');

  // A token minted without an account is forty-eight characters with no dashes; it is refused on
  // photographs, so taking it for a password would fail later and further from the cause.
  assert.equal(appPasswordIn('0123456789abcdef0123456789abcdef0123456789abcdef'), null);
  assert.equal(appPasswordIn(''), null);
});
