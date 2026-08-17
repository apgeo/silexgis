// SPDX-License-Identifier: AGPL-3.0-or-later
import i18next from 'i18next';
import { describe, expect, it } from 'vitest';
import type {
  AccessDomainName,
  AccessScopeKind,
  ActivityState,
  SearchDocumentItem,
  TerrainBuildPhase,
  TerrainBuildSourceKind,
  TerrainBuildStatus,
} from '../api/hooks.ts';
import { RESLINK_ANCHOR_KINDS, RESLINK_TARGET_TYPES } from '../components/reslinks/registry.ts';
import {
  SEEDED_RELATION_CODES,
  DIRECTED_RELATION_CODES,
  TRIP_ROLE_CODES,
} from '../components/reslinks/relations.ts';
import {
  SEEDED_TRIP_SECTION_ENUM_VALUES,
  SEEDED_TRIP_SECTION_FIELD_CODES,
} from '../components/trips/tripSectionFields.ts';
import { SEEDED_PARTICIPANT_ROLE_CODES } from '../components/trips/participantRoles.ts';
import { SEEDED_TRIP_TYPE_CODES } from '../components/trips/tripTypes.ts';
import { TERRAIN_PROBLEM_MESSAGE_KEYS } from '../pages/admin/terrain/terrainProblems.ts';
import type { TerrainDepthBand } from '../pages/admin/terrain/terrainDepth.ts';
import en from './locales/en.json';
import ro from './locales/ro.json';

function flattenKeys(value: object, prefix = ''): string[] {
  return Object.entries(value).flatMap(([key, child]) =>
    typeof child === 'object' && child !== null
      ? flattenKeys(child, `${prefix}${key}.`)
      : [`${prefix}${key}`],
  );
}

/**
 * Every source file's text, read through the bundler rather than the filesystem so this
 * stays inside the app's own module world (the browser type project has no `node:fs`).
 */
const sourceText = import.meta.glob('../**/*.{ts,tsx}', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

function lookup(locale: object, key: string): unknown {
  return key
    .split('.')
    .reduce<unknown>(
      (node, part) =>
        typeof node === 'object' && node !== null ? (node as Record<string, unknown>)[part] : undefined,
      locale,
    );
}

/**
 * Every resource domain the server publishes, listed so the labels can be checked against
 * it. The type makes the list exhaustive in both directions: a domain added on the server
 * fails to compile here until it is named, and a name that is no longer a domain fails too.
 * Without this, a new domain would reach the rules editor and the permission preview
 * showing its own lookup key where its name belongs.
 */
const accessDomains: Record<AccessDomainName, true> = {
  features: true,
  tripLogs: true,
  geofiles: true,
  georeferencedMaps: true,
  mapViews: true,
  files: true,
  documents: true,
  mapLayers: true,
  tags: true,
  hierarchies: true,
  taxonomies: true,
  cavers: true,
  cavingGroups: true,
  users: true,
  permissionGroups: true,
  featureSets: true,
  settings: true,
  messageTemplates: true,
  audit: true,
  jobs: true,
  terrain: true,
};

/**
 * The same exhaustiveness, for what a rule can be scoped to. The rules editor builds its
 * scope list from the server's catalogue and labels each option by looking the scope kind
 * up here, so a scope added on the server ships as a raw lookup key until it is named —
 * silently, because nothing else in the client mentions the vocabulary.
 */
const accessScopeKinds: Record<AccessScopeKind, true> = {
  all: true,
  own: true,
  cavingGroup: true,
  subtree: true,
  featureSet: true,
  cabinet: true,
  object: true,
};

/**
 * The divisions a content hit can honestly be placed in. "whole" is deliberately absent: it
 * means the format numbers nothing, and the whole point of the distinction is that such a hit
 * is shown without a position rather than with an invented one. A label for it would be the
 * lie the type exists to prevent, so this list must stay one shorter than the server's enum.
 */
type NumberedDivision = Exclude<SearchDocumentItem['division'], 'whole'>;
const numberedDivisions: Record<NumberedDivision, true> = {
  page: true,
  sheet: true,
  slide: true,
};

/**
 * Every lifecycle state the server publishes. The badge looks its label up by the value it was
 * given, so an unnamed state ships as a raw lookup key where a word belongs — and the states a
 * trip cannot hold today are named too, because they belong to the same vocabulary and will
 * start arriving without any client change when the activities that use them land.
 */
const activityStates: Record<ActivityState, true> = {
  draft: true,
  proposed: true,
  planned: true,
  confirmed: true,
  done: true,
  published: true,
  cancelled: true,
  delayed: true,
};

/**
 * The vocabularies a terrain build is described by. The list and the pipeline view look each
 * label up by the value the server sent, so an unnamed one ships as a raw lookup key in the column
 * that says whether a build worked — and the scanner that checks written-out keys cannot see these,
 * because they are built from the value rather than typed out.
 */
const terrainBuildStatuses: Record<TerrainBuildStatus, true> = {
  queued: true,
  running: true,
  succeeded: true,
  failed: true,
};

const terrainBuildPhases: Record<TerrainBuildPhase, true> = {
  pending: true,
  fetch: true,
  prepare: true,
  bake: true,
  validate: true,
  publish: true,
};

const terrainSourceKinds: Record<TerrainBuildSourceKind, true> = {
  fetched: true,
  uploaded: true,
  serverDirectory: true,
};

/**
 * The sentence that says what a chosen depth is asking for is looked up the same way, from a band
 * this application decides rather than from anything the server sends. It is the one line on the
 * form that explains the number, so an unnamed band renders as its own key exactly where somebody
 * is trying to understand what they are about to ask for.
 */
const terrainDepthBands: Record<TerrainDepthBand, true> = {
  coarse: true,
  coverage: true,
  fine: true,
  survey: true,
};

// EN and RO must be maintained together.
describe('i18n locales', () => {
  it('en and ro define exactly the same keys', () => {
    expect(flattenKeys(ro).sort()).toEqual(flattenKeys(en).sort());
  });

  it('no empty translations', () => {
    const empty = (locale: object) =>
      flattenKeys(locale).filter((key) =>
        key.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown>)[part], locale) === '',
      );
    expect(empty(en)).toEqual([]);
    expect(empty(ro)).toEqual([]);
  });

  it('every access domain the server publishes is named in both locales', () => {
    const names = Object.keys(accessDomains);
    const enDomains: Record<string, string> = en.access.domains;
    const roDomains: Record<string, string> = ro.access.domains;
    expect(names.filter((name) => !enDomains[name])).toEqual([]);
    expect(names.filter((name) => !roDomains[name])).toEqual([]);
    // The reverse direction too: a leftover label for a domain the server dropped would
    // sit unnoticed in both files forever.
    expect(Object.keys(enDomains).sort()).toEqual(names.sort());
  });

  it('every terrain build status, phase, source kind and depth band is named in both locales', () => {
    const cases: [string[], Record<string, string>, Record<string, string>][] = [
      [Object.keys(terrainBuildStatuses), en.terrain.statuses, ro.terrain.statuses],
      [Object.keys(terrainBuildPhases), en.terrain.phases, ro.terrain.phases],
      [Object.keys(terrainSourceKinds), en.terrain.sourceKinds, ro.terrain.sourceKinds],
      [Object.keys(terrainDepthBands), en.terrain.depthBands, ro.terrain.depthBands],
    ];
    for (const [names, enNames, roNames] of cases) {
      expect(names.filter((name) => !enNames[name])).toEqual([]);
      expect(names.filter((name) => !roNames[name])).toEqual([]);
      // And the reverse: a label kept for a value the server no longer sends would sit unnoticed.
      expect(Object.keys(enNames).sort()).toEqual(names.sort());
      expect(Object.keys(roNames).sort()).toEqual(names.sort());
    }
  });

  /**
   * The refusals the terrain endpoints answer with are worded through a lookup table rather than
   * written out one by one at the call, so the scan below — which reads keys straight out of the
   * source text — cannot see any of them. Without this, a refusal nobody translated would appear
   * on screen as its own key at the exact moment somebody needs to be told why the server said no.
   */
  it('every terrain refusal has wording in both locales, and none is left over', () => {
    const keys = Object.values(TERRAIN_PROBLEM_MESSAGE_KEYS);
    expect(keys.length).toBeGreaterThan(10);
    for (const locale of [en, ro]) {
      expect(keys.filter((key) => typeof lookup(locale, key) !== 'string')).toEqual([]);
    }
    // And the reverse: wording kept for a code the server no longer answers with would sit here
    // unread, and nothing else would ever say so.
    const named = keys.map((key) => key.replace('terrain.problems.', '')).sort();
    expect(Object.keys(en.terrain.problems).sort()).toEqual(named);
    expect(Object.keys(ro.terrain.problems).sort()).toEqual(named);
  });

  it('every access scope kind the server publishes is named in both locales', () => {
    const kinds = Object.keys(accessScopeKinds);
    const enScopes: Record<string, string> = en.access.scopes;
    const roScopes: Record<string, string> = ro.access.scopes;
    expect(kinds.filter((kind) => !enScopes[kind])).toEqual([]);
    expect(kinds.filter((kind) => !roScopes[kind])).toEqual([]);
    expect(Object.keys(enScopes).sort()).toEqual(kinds.sort());
  });

  it('every activity state the server publishes is named in both locales', () => {
    const states = Object.keys(activityStates);
    const enStates: Record<string, string> = en.trips.stateValues;
    const roStates: Record<string, string> = ro.trips.stateValues;
    expect(states.filter((state) => !enStates[state])).toEqual([]);
    expect(states.filter((state) => !roStates[state])).toEqual([]);
    // And the reverse: a label kept for a state the server no longer has.
    expect(Object.keys(enStates).sort()).toEqual(states.sort());
  });

  // A link chip labels its target by type. An unnamed type would render as a raw lookup
  // key beside the title, in both languages, on every surface links appear on.
  it('every link target type is named in both locales', () => {
    const expected = [...RESLINK_TARGET_TYPES, 'unknown'].sort();
    for (const locale of [en, ro]) {
      expect(Object.keys(locale.resLinks.targetTypes).sort()).toEqual(expected);
    }
  });

  // The picker names every anchor kind, including the ones whose editor has not shipped —
  // they are listed disabled, and a disabled row with a raw lookup key for a label would
  // say nothing about what is coming.
  it('every anchor kind is named in both locales', () => {
    const expected = [...RESLINK_ANCHOR_KINDS].sort();
    for (const locale of [en, ro]) {
      expect(Object.keys(locale.resLinks.anchorKinds).sort()).toEqual(expected);
    }
  });

  // Relation wording is translated by code for the vocabulary that ships with the app;
  // only the rows an installation adds itself are shown as stored.
  it('every shipped relation code has wording, and every directed one reads both ways', () => {
    const shipped: readonly string[] = SEEDED_RELATION_CODES;
    for (const locale of [en, ro]) {
      const relations = locale.resLinks.relations as unknown as Record<
        string,
        { name?: string; inverse?: string } | string
      >;
      for (const code of SEEDED_RELATION_CODES) {
        const wording = relations[code];
        expect(typeof wording === 'object' && Boolean(wording.name), code).toBe(true);
        const inverse = typeof wording === 'object' ? wording.inverse : undefined;
        expect(Boolean(inverse), code).toBe(DIRECTED_RELATION_CODES.includes(code));
      }
      // The reverse direction: wording left behind for a code the app no longer ships.
      expect(
        Object.keys(relations).filter((key) => key !== 'unspecified' && !shipped.includes(key)),
      ).toEqual([]);
    }
  });

  // What a trip was for is a row an installation may extend, and the rows that ship are
  // translated by code — exactly like the relation vocabulary. Both directions matter: a
  // shipped code with no wording renders as a raw key, and wording left behind for a code the
  // application no longer ships is an offer nothing can take up.
  it('every shipped trip purpose has wording in both locales, and no wording outlives its code', () => {
    const expected = [...SEEDED_TRIP_TYPE_CODES].sort();
    for (const locale of [en, ro]) {
      expect(Object.keys(locale.trips.typeValues).sort()).toEqual(expected);
    }
  });

  // What somebody did on a trip is the same kind of vocabulary: rows an installation may extend,
  // with the shipped ones translated by code. A shipped code with no wording renders as a raw key
  // beside a person's name, and wording left behind for a code the application no longer ships is
  // an offer nothing can take up.
  it('every shipped participant role has wording in both locales, and no wording outlives its code', () => {
    const expected = [...SEEDED_PARTICIPANT_ROLE_CODES].sort();
    for (const locale of [en, ro]) {
      expect(Object.keys(locale.trips.participantRoleValues).sort()).toEqual(expected);
    }
  });

  // A trip purpose carries JSON schemas whose field titles are one string each, written in
  // whatever language their author was working in. The codes the product ships are therefore
  // translated by code, the same way every other shipped vocabulary is — so a missing one puts
  // the schema's own language on a screen in another, and one left behind is wording that
  // overrides a title nothing writes any more.
  it('every shipped trip report field has wording in both locales, and no wording outlives its code', () => {
    const expected = [...SEEDED_TRIP_SECTION_FIELD_CODES].sort();
    for (const locale of [en, ro]) {
      expect(Object.keys(locale.trips.sectionFields).sort()).toEqual(expected);
    }
  });

  // A choice offers values, and a value is a code like any other. A translated label above a
  // list of English tokens is only half a translation, so the values the product ships are
  // worded in both locales too — and wording left behind for a value nothing offers any more is
  // an option a reader can never be shown.
  it('every shipped trip report choice has its values worded in both locales', () => {
    for (const locale of [en, ro]) {
      const worded = (locale.trips as Record<string, unknown>).sectionValues as Record<
        string,
        Record<string, string>
      >;
      expect(Object.keys(worded).sort()).toEqual(Object.keys(SEEDED_TRIP_SECTION_ENUM_VALUES).sort());
      for (const [field, values] of Object.entries(SEEDED_TRIP_SECTION_ENUM_VALUES)) {
        expect(Object.keys(worded[field]).sort(), field).toEqual([...values].sort());
      }
    }
  });

  // One unit is stored and the reader's locale decides how the figure is written, which is the
  // whole reason a metre column carries no unit beside it. A raw JavaScript number would put a
  // decimal point and a thousands comma on a screen that uses neither.
  it('writes a measured figure in the reader’s own number formatting', async () => {
    const probe = i18next.createInstance();
    await probe.init({
      lng: 'ro',
      resources: { ro: { translation: ro }, en: { translation: en } },
      interpolation: { escapeValue: false },
    });

    expect(probe.t('trips.metres', { value: 1284.5 })).toBe('1.284,5 m');
    await probe.changeLanguage('en');
    expect(probe.t('trips.metres', { value: 1284.5 })).toBe('1,284.5 m');
  });

  // The counted facts are labelled by the name they are stored under, so a column and its
  // label cannot drift apart without this saying so.
  it('every counted fact a trip records is labelled in both locales', () => {
    for (const locale of [en, ro]) {
      for (const key of ['depthReachedM', 'lengthSurveyedM', 'surveyStations', 'ropeMetres']) {
        expect(Boolean((locale.trips as Record<string, unknown>)[key]), key).toBe(true);
      }
    }
  });

  // A trip draws each of its roles as a field of its own, titled by what the field holds
  // rather than by the verb the relation reads as. The title is looked up from the code, so
  // a missing one shows as a raw key where a heading belongs.
  it('every trip role field is titled in both locales', () => {
    const expected = [...TRIP_ROLE_CODES].sort();
    for (const locale of [en, ro]) {
      expect(Object.keys(locale.trips.roles).sort()).toEqual(expected);
    }
  });

  /**
   * A key that was never added is not a crash and not a blank: the translator falls back to
   * the key itself, so the screen quietly reads "common.back" where a word belongs. Nothing
   * else here catches that — the two files can agree perfectly and still be missing a key the
   * code asks for. Only literal calls are checked; keys built from a template are covered by
   * the exhaustiveness tests above, which is why those exist.
   */
  it('every translation key the code asks for by name exists', () => {
    const asked = Object.entries(sourceText)
      .filter(([file]) => !file.includes('.test.'))
      .flatMap(([file, text]) =>
        [...text.matchAll(/\bt\(\s*'([\w.]+)'/g)].map((match) => ({
          key: match[1],
          where: file.replace('../', ''),
        })),
      );
    // A guard on the reader itself: if the pattern ever stops matching, an empty result would
    // pass this test silently while checking nothing.
    expect(asked.length).toBeGreaterThan(200);
    expect(
      asked
        .filter(({ key }) => typeof lookup(en, key) !== 'string')
        .map(({ key, where }) => `${where}: ${key}`),
    ).toEqual([]);
  });

  it('names every numbered division a content hit can carry, and no more', () => {
    const names = Object.keys(numberedDivisions);
    const enDivisions: Record<string, string> = en.search.divisions;
    const roDivisions: Record<string, string> = ro.search.divisions;
    expect(names.filter((name) => !enDivisions[name])).toEqual([]);
    expect(names.filter((name) => !roDivisions[name])).toEqual([]);
    expect(Object.keys(enDivisions).sort()).toEqual(names.sort());
  });
});
