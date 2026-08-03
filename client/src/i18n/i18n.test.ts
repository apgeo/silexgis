// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { AccessDomainName, AccessScopeKind } from '../api/hooks.ts';
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

  it('every access scope kind the server publishes is named in both locales', () => {
    const kinds = Object.keys(accessScopeKinds);
    const enScopes: Record<string, string> = en.access.scopes;
    const roScopes: Record<string, string> = ro.access.scopes;
    expect(kinds.filter((kind) => !enScopes[kind])).toEqual([]);
    expect(kinds.filter((kind) => !roScopes[kind])).toEqual([]);
    expect(Object.keys(enScopes).sort()).toEqual(kinds.sort());
  });
});
