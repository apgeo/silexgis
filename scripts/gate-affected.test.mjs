// SPDX-License-Identifier: AGPL-3.0-or-later

// Tests for the affected-classes selector: how a changed path picks its owning integration
// classes, where selection fails closed to the full suite, and — the load-bearing half —
// that the map cannot rot: every source area must be claimed, every class the map names must
// exist, and the two cross-cutting lists must equal what re-deriving them from the test
// sources produces today. A new feature directory or a new 403-asserting class fails these
// tests until the map admits it.
//
// Run from the repository root with `node --test "scripts/**/*.test.mjs"`.

import { strict as assert } from 'node:assert';
import { readFileSync, readdirSync, existsSync, statSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';

import { classify, filterExpression } from './gate-affected.mjs';
import * as map from './gate-affected.map.mjs';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const testDir = join(repoRoot, 'server', 'tests', 'SilexGis.Api.Tests');

describe('classification', () => {
  it('a feature file selects its owning classes', () => {
    const r = classify(['server/src/SilexGis.Api/Features/Cabinets/CabinetEndpoints.cs'], map);
    assert.equal(r.mode, 'targeted');
    assert.deepEqual(r.classes, ['CabinetApiTests', 'CabinetTreeTests']);
  });

  it('a Domain area routes to the same group as its feature slice', () => {
    const feature = classify(['server/src/SilexGis.Api/Features/TripLogs/TripEndpoints.cs'], map);
    const domain = classify(['server/src/SilexGis.Domain/Trips/TripPlan.cs'], map);
    assert.equal(domain.mode, 'targeted');
    assert.deepEqual(domain.classes, feature.classes);
  });

  it('an unclaimed server path fails closed to the full suite', () => {
    const r = classify(['server/src/SilexGis.Api/SomeNewTopLevelThing.cs'], map);
    assert.equal(r.mode, 'full');
    assert.deepEqual(r.classes, []);
    assert.ok(r.reasons.some((x) => x.includes('not claimed')));
  });

  it('an area mapped to no owning classes fails closed to the full suite', () => {
    const r = classify(['server/src/SilexGis.Api/Features/MapLayers/MapLayerEndpoints.cs'], map);
    assert.equal(r.mode, 'full');
    assert.ok(r.reasons.some((x) => x.includes('no owning classes')));
  });

  it('shared test infrastructure fails closed to the full suite', () => {
    const r = classify(['server/tests/SilexGis.Api.Tests/Support/PostgresFixture.cs'], map);
    assert.equal(r.mode, 'full');
  });

  it('a touched test class selects itself', () => {
    const r = classify(['server/tests/SilexGis.Api.Tests/GeofileTests.cs'], map);
    assert.equal(r.mode, 'targeted');
    assert.deepEqual(r.classes, ['GeofileTests']);
  });

  it('access-rule changes add every denial- and obfuscation-asserting class', () => {
    const r = classify(['server/src/SilexGis.Domain/Access/AccessWalk.cs'], map);
    assert.equal(r.mode, 'full'); // Domain/Access is also whole-API blast radius
    const trig = classify(['server/src/SilexGis.Api/Features/Permissions/GrantEndpoints.cs'], map);
    assert.equal(trig.mode, 'targeted');
    for (const c of map.crossCutting.permissionClasses) assert.ok(trig.classes.includes(c), c);
    for (const c of map.crossCutting.locationClasses) assert.ok(trig.classes.includes(c), c);
  });

  it('client-only and deployment-only changes select nothing', () => {
    const r = classify(['client/src/pages/TripPage.tsx', 'deploy/docker-compose.yml'], map);
    assert.equal(r.mode, 'none');
    assert.deepEqual(r.classes, []);
  });

  it('one whole-API path makes the verdict full whatever else the diff contains', () => {
    const r = classify(
      [
        'server/src/SilexGis.Api/Features/Cabinets/CabinetEndpoints.cs',
        'server/src/SilexGis.Infrastructure/Persistence/AppDbContext.cs',
      ],
      map,
    );
    assert.equal(r.mode, 'full');
  });

  it('the filter expression pins each class with the namespace and a trailing dot', () => {
    const f = filterExpression(['CalendarTests']);
    assert.equal(f, 'FullyQualifiedName~SilexGis.Api.Tests.CalendarTests.');
    assert.ok(!f.includes('CalendarWindowTests'));
  });
});

describe('the map cannot rot', () => {
  const claimed = (p) =>
    Object.keys(map.areas).some((a) => p.startsWith(a)) ||
    map.full.some((a) => p.startsWith(a)) ||
    map.crossCutting.triggers.some((a) => p.startsWith(a));

  const areaDirs = (base, rel) =>
    readdirSync(join(base, rel))
      .filter((d) => !['bin', 'obj'].includes(d))
      .filter((d) => statSync(join(base, rel, d)).isDirectory())
      .map((d) => `${rel.replaceAll('\\', '/')}/${d}/`);

  it('every Api feature directory is claimed', () => {
    for (const d of areaDirs(repoRoot, 'server/src/SilexGis.Api/Features')) {
      assert.ok(claimed(d), `${d} is not claimed by the map — add it (or list it as full)`);
    }
  });

  it('every Domain and Infrastructure area directory is claimed', () => {
    for (const layer of ['server/src/SilexGis.Domain', 'server/src/SilexGis.Infrastructure']) {
      for (const d of areaDirs(repoRoot, layer)) {
        assert.ok(claimed(d), `${d} is not claimed by the map — add it (or list it as full)`);
      }
    }
  });

  it('every class the map names exists as a test source file', () => {
    const everywhere = [
      ...Object.values(map.groups).flat(),
      ...map.crossCutting.permissionClasses,
      ...map.crossCutting.locationClasses,
    ];
    for (const c of new Set(everywhere)) {
      assert.ok(existsSync(join(testDir, `${c}.cs`)), `${c}.cs does not exist under the test project`);
    }
  });

  it('every area names a group that exists', () => {
    for (const [prefix, owner] of Object.entries(map.areas)) {
      if (typeof owner === 'string') {
        assert.ok(map.groups[owner], `${prefix} names unknown group '${owner}'`);
      }
    }
  });

  it('the cross-cutting lists equal what re-deriving them produces today', () => {
    const perm = new RegExp(map.crossCutting.derivation.permission.source, map.crossCutting.derivation.permission.flags);
    const loc = new RegExp(map.crossCutting.derivation.location.source, map.crossCutting.derivation.location.flags);
    const permFound = [];
    const locFound = [];
    for (const f of readdirSync(testDir).filter((x) => x.endsWith('Tests.cs'))) {
      const text = readFileSync(join(testDir, f), 'utf8');
      const cls = f.replace(/\.cs$/, '');
      if (perm.test(text)) permFound.push(cls);
      if (loc.test(text)) locFound.push(cls);
    }
    permFound.sort();
    locFound.sort();
    assert.deepEqual(
      [...map.crossCutting.permissionClasses].sort(),
      permFound,
      'permissionClasses drifted from the sources — re-derive the list and update the map',
    );
    assert.deepEqual(
      [...map.crossCutting.locationClasses].sort(),
      locFound,
      'locationClasses drifted from the sources — re-derive the list and update the map',
    );
  });
});
