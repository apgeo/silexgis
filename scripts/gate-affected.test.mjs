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

import {
  classify,
  deriveCrossCutting,
  filterExpression,
  withCrossCutting,
} from './gate-affected.mjs';
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

  // The mirror of the test above, and the half that was missing while a class could exist that
  // no group named. That gap is invisible from either direction on its own: naming a class that
  // does not exist is caught above, but a class nobody names simply never runs in the targeted
  // tier, and the change that stops running it looks like an ordinary addition to the map.
  //
  // Deliberately empty since 2026-10-05. It held the ten classes no group named on the day this
  // test was written, and for a month they were skipped by the targeted tier whenever their own
  // area changed. Every one now has an owner. The set stays so the check below keeps its shape —
  // and so that putting a NEW class here, rather than in a group, is visibly the wrong move.
  const unclaimedClasses = new Set([]);

  it('every integration test class is named by the map', () => {
    const named = new Set([
      ...Object.values(map.groups).flat(),
      ...map.crossCutting.permissionClasses,
      ...map.crossCutting.locationClasses,
    ]);
    const orphans = readdirSync(testDir)
      .filter((f) => f.endsWith('Tests.cs'))
      .map((f) => f.replace(/\.cs$/, ''))
      .filter((c) => !named.has(c) && !unclaimedClasses.has(c));
    assert.deepEqual(
      orphans,
      [],
      `no group or cross-cutting list names ${orphans.join(', ')} — the targeted tier would never run it`,
    );
  });

  it('every exempted class still exists, so the exemption list cannot outlive its holes', () => {
    for (const c of unclaimedClasses) {
      assert.ok(
        existsSync(join(testDir, `${c}.cs`)),
        `${c}.cs is gone — drop it from the exemption list`,
      );
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
    const derived = deriveCrossCutting(map, testDir);
    const how = 'run `node scripts/gate-affected.mjs --rederive` and commit the map';
    assert.deepEqual(
      [...map.crossCutting.permissionClasses].sort(),
      derived.permissionClasses,
      `permissionClasses drifted from the sources — ${how}`,
    );
    assert.deepEqual(
      [...map.crossCutting.locationClasses].sort(),
      derived.locationClasses,
      `locationClasses drifted from the sources — ${how}`,
    );
  });

  it('rewriting the lists replaces both and nothing else', () => {
    const source = [
      'export const before = 1;',
      'export const crossCutting = {',
      "  triggers: ['x/'],",
      '  permissionClasses: [',
      " 'OldA', 'OldB',",
      '  ],',
      '  locationClasses: [',
      " 'OldC',",
      '  ],',
      '};',
      'export const after = 2;',
    ].join('\n');
    const out = withCrossCutting(source, { permissionClasses: ['A', 'B'], locationClasses: [] });
    assert.match(out, /permissionClasses: \[\n {4}'A', 'B',\n {2}\],/);
    assert.match(out, /locationClasses: \[\n\n {2}\],|locationClasses: \[\n {2}\],/);
    assert.ok(!out.includes('Old'), 'the old names are gone');
    assert.ok(out.startsWith('export const before = 1;') && out.endsWith('export const after = 2;'));
    assert.throws(() => withCrossCutting('nothing here', { permissionClasses: [], locationClasses: [] }));
  });
});
