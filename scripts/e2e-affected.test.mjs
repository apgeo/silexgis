// SPDX-License-Identifier: AGPL-3.0-or-later
import assert from 'node:assert/strict';
import { readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';

import * as map from './e2e-affected.map.mjs';
import * as serverMap from './gate-affected.map.mjs';
import { select } from './e2e-affected.mjs';

const e2eDir = join(dirname(fileURLToPath(import.meta.url)), '..', 'client', 'e2e');
const onDisk = readdirSync(e2eDir).filter((name) => name.endsWith('.spec.ts')).sort();
const chosen = (paths) => select(paths, onDisk);

describe('the map of browser specs cannot rot', () => {
  it('every spec on disk is named by an area, so a change can reach it', () => {
    const named = new Set([...map.always, ...Object.values(map.specs).flat()]);
    assert.deepEqual(onDisk.filter((name) => !named.has(name)), []);
  });

  it('every spec an area names exists', () => {
    const missing = [...map.always, ...Object.values(map.specs).flat()].filter((name) => !onDisk.includes(name));
    assert.deepEqual([...new Set(missing)], []);
  });

  it('every area the server map names has a line here, even an empty one', () => {
    const areas = new Set(Object.values(serverMap.areas).filter((value) => typeof value === 'string'));
    assert.deepEqual([...areas].filter((area) => !(area in map.specs)).sort(), []);
  });

  it('every area a client path points at has a line here', () => {
    const areas = new Set(Object.values(map.clientAreas).flat());
    assert.deepEqual([...areas].filter((area) => !(area in map.specs)).sort(), []);
  });
});

describe('a change selects the specs that answer for it', () => {
  it('a page selects its own specs and the smoke spec, and nothing else', () => {
    const { whole, specs } = chosen(['client/src/pages/calendar/CalendarPage.tsx']);
    assert.equal(whole, false);
    assert.deepEqual(specs, ['calendar-feed.spec.ts', 'calendar.spec.ts', 'smoke.spec.ts']);
  });

  it('a server feature is read through the server map, under the same name', () => {
    const { whole, specs } = chosen(['server/src/SilexGis.Api/Features/Checklists/ChecklistEndpoints.cs']);
    assert.equal(whole, false);
    assert.deepEqual(specs, ['checklists.spec.ts', 'smoke.spec.ts']);
  });

  it('the longer prefix wins: a directory of components is its area, the shell beside it is everything', () => {
    assert.equal(chosen(['client/src/components/trips/TripForm.tsx']).whole, false);
    const shell = chosen(['client/src/components/AppLayout.tsx']);
    assert.equal(shell.whole, true);
    assert.match(shell.reasons[0], /every page leans on it/);
  });

  it('a changed spec selects itself, and a helper every spec shares selects everything', () => {
    assert.deepEqual(chosen(['client/e2e/gallery.spec.ts']).specs, ['gallery.spec.ts', 'smoke.spec.ts']);
    assert.equal(chosen(['client/e2e/helpers.ts']).whole, true);
  });

  it('fails closed: an unclaimed source path, a whole-API path and an area with no narrower spec', () => {
    assert.equal(chosen(['client/src/somethingNew/Thing.tsx']).whole, true);
    assert.equal(chosen(['server/src/SilexGis.Api/Program.cs']).whole, true);
    assert.equal(chosen(['server/src/SilexGis.Api/Features/Taxonomies/TaxonomyEndpoints.cs']).whole, true);
    assert.equal(chosen(['some-new-file-at-the-root.txt']).whole, true);
  });

  it('what no browser spec answers for selects only the smoke spec', () => {
    const { whole, specs } = chosen([
      'docs/user-guide.md', 'scripts/gate-lock.mjs', 'server/tests/SilexGis.Api.Tests/CalendarTests.cs',
      'client/src/pages/trips/TripListPage.test.tsx',
    ]);
    assert.equal(whole, false);
    assert.deepEqual(specs, ['smoke.spec.ts']);
  });
});
