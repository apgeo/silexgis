// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import type { AnchorKind, ResLinkAnchorState } from '../../api/hooks.ts';
import {
  admittedAnchorKinds,
  anchorKindEntry,
  anchorProblem,
  anchorStateNote,
  anchorSummary,
  canComposeAnchor,
  formatMediaTime,
  isAnchorKind,
  isResLinkTargetType,
  linkPageRoute,
  memberRoute,
  RESLINK_ANCHOR_KINDS,
  RESLINK_TARGET_TYPES,
  targetTypeEntry,
} from './registry.ts';
import { DIRECTED_RELATION_CODES, relationPhrase, relationPhraseFor, SEEDED_RELATION_CODES } from './relations.ts';
import type { ResLink, ResLinkMember, ResLinkRelationType } from '../../api/hooks.ts';

const t = i18n.getFixedT('en');

/** Every anchor kind the wire can carry, so the tables below stay exhaustive by type. */
const allAnchorKinds: Record<AnchorKind, true> = {
  whole: true,
  textRange: true,
  page: true,
  pageRange: true,
  imageRegion: true,
  timePoint: true,
  timeRange: true,
  modelStation: true,
  modelStationRange: true,
  modelSurvey: true,
  modelSurveyRange: true,
  modelPoint: true,
  waypoint: true,
  waypointRange: true,
};

function member(overrides: Partial<ResLinkMember> = {}): ResLinkMember {
  return {
    id: 'm1',
    targetType: 'feature',
    targetId: 'f1',
    isMain: false,
    sortOrder: 0,
    note: null,
    anchorKind: 'whole',
    anchor: null,
    anchorFileId: null,
    anchorState: 'exact',
    display: { title: 'Something', subtitle: null, route: null, thumbnailUrl: null },
    ...overrides,
  };
}

describe('member type registry', () => {
  it('names and icons every target type the server accepts', () => {
    for (const type of RESLINK_TARGET_TYPES) {
      expect(isResLinkTargetType(type)).toBe(true);
      const entry = targetTypeEntry(type);
      expect(entry.icon).toBeTypeOf('object');
      // A missing translation comes back as the lookup key itself.
      expect(t(entry.labelKey)).not.toBe(entry.labelKey);
    }
  });

  it('falls back to a generic entry for a type it has never heard of', () => {
    expect(isResLinkTargetType('quantumTunnel')).toBe(false);
    const entry = targetTypeEntry('quantumTunnel');
    expect(t(entry.labelKey)).toBe('Item');
    expect(entry.route).toBeNull();
  });

  it('prefers the route the server resolved over its own guess', () => {
    // A cave belongs on the cave page even though it is a feature to the vocabulary.
    expect(
      memberRoute('feature', 'f1', { title: 'Cave', subtitle: null, route: '/caves/f1', thumbnailUrl: null }),
    ).toBe('/caves/f1');
    expect(memberRoute('feature', 'f1', null)).toBe('/features/f1');
  });

  it('routes a document to its own page even though the server names none', () => {
    expect(
      memberRoute('document', 'd1', { title: 'Scan', subtitle: null, route: null, thumbnailUrl: null }),
    ).toBe('/documents/d1');
  });

  it('routes a camp to its own page, from either side', () => {
    // Both halves, because either one alone decides where a chip goes: the server names the
    // route for a camp it resolved, and this table answers for one it did not.
    expect(memberRoute('expedition', 'e1', null)).toBe('/expeditions/e1');
    expect(
      memberRoute('expedition', 'e1', {
        title: 'Bihor summer camp',
        subtitle: null,
        route: '/expeditions/e1',
        thumbnailUrl: null,
      }),
    ).toBe('/expeditions/e1');
  });

  it('leaves a chip un-navigable when neither side has a page for it', () => {
    expect(memberRoute('cabinet', 'c1', null)).toBeNull();
    expect(
      memberRoute('caver', 'p1', { title: 'Someone', subtitle: null, route: null, thumbnailUrl: null }),
    ).toBeNull();
  });

  it('addresses a link page by its short code', () => {
    expect(linkPageRoute('Ab3xY9Zq')).toBe('/links/Ab3xY9Zq');
  });
});

describe('anchor summaries', () => {
  it('has an entry for every anchor kind, and only "whole" declines to summarise', () => {
    for (const kind of Object.keys(allAnchorKinds) as AnchorKind[]) {
      expect(isAnchorKind(kind)).toBe(true);
      const entry = anchorKindEntry(kind);
      expect(entry).not.toBeNull();
      expect(entry?.summary === null).toBe(kind === 'whole');
    }
  });

  it('renders each kind compactly', () => {
    expect(anchorSummary('whole', null, t)).toBeNull();
    expect(anchorSummary('page', { page: 7 }, t)).toBe('p. 7');
    expect(anchorSummary('pageRange', { fromPage: 3, toPage: 9 }, t)).toBe('pp. 3–9');
    expect(anchorSummary('timePoint', { t: 55 }, t)).toBe('0:55');
    expect(anchorSummary('timeRange', { start: 55, end: 80 }, t)).toBe('0:55–1:20');
    expect(anchorSummary('modelStation', { station: 'p12' }, t)).toBe('station p12');
    expect(anchorSummary('modelSurveyRange', { fromSurvey: 'intrare', toSurvey: 'lac' }, t)).toBe(
      'surveys intrare→lac',
    );
    expect(anchorSummary('imageRegion', { shape: 'rect' }, t)).toBe('region');
    expect(anchorSummary('waypoint', { index: 4 }, t)).toBe('waypoint 4');
    expect(anchorSummary('waypoint', { index: 4, name: 'Izvor' }, t)).toBe('waypoint Izvor');
    expect(anchorSummary('waypointRange', { fromIndex: 4, toIndex: 9 }, t)).toBe('waypoints 4–9');
    expect(anchorSummary('textRange', { start: 1, end: 9, quote: 'x' }, t)).toBe('quote');
    expect(anchorSummary('textRange', { page: 2, start: 1, end: 9, quote: 'x' }, t)).toBe('quote, p. 2');
  });

  it('never leaves a lookup key or an exception in a chip', () => {
    for (const kind of Object.keys(allAnchorKinds) as AnchorKind[]) {
      for (const payload of [null, undefined, {}, 'nonsense', 42, { page: 'seven' }]) {
        const summary = anchorSummary(kind, payload, t);
        if (kind === 'whole') {
          expect(summary).toBeNull();
        } else {
          expect(summary).toBeTruthy();
          expect(summary).not.toContain('resLinks.');
        }
      }
    }
  });

  it('degrades an unknown kind to a generic part label rather than breaking', () => {
    expect(anchorSummary('holographicSlice', { anything: true }, t)).toBe('part');
  });

  it('formats media positions the way a scrubber reads them', () => {
    expect(formatMediaTime(0)).toBe('0:00');
    expect(formatMediaTime(9)).toBe('0:09');
    expect(formatMediaTime(75)).toBe('1:15');
    expect(formatMediaTime(3725)).toBe('1:02:05');
    expect(formatMediaTime(-4)).toBe('0:00');
  });

  it('says nothing about an exact anchor and names every other state', () => {
    expect(anchorStateNote('exact', t)).toBeNull();
    for (const state of ['reanchored', 'degraded', 'unresolvable'] as ResLinkAnchorState[]) {
      const note = anchorStateNote(state, t);
      expect(note).toBeTruthy();
      expect(note).not.toContain('resLinks.');
    }
  });
});

describe('relation phrasing', () => {
  const relation = (overrides: Partial<ResLinkRelationType>): ResLinkRelationType => ({
    id: 1,
    code: 'contains',
    name: 'Contains',
    description: null,
    sortOrder: 30,
    directed: true,
    inverseName: 'Contained in',
    seeded: true,
    ...overrides,
  });

  it('translates the shipped vocabulary by code, in both directions where it has two', () => {
    expect(relationPhrase(relation({}), 'forward', t)).toBe('Contains');
    expect(relationPhrase(relation({}), 'inverse', t)).toBe('Contained in');
    // The stored wording reads from the main member outwards, which for "documents" means
    // the main member is the thing being documented.
    expect(relationPhrase(relation({ code: 'documents' }), 'forward', t)).toBe('Documented by');
    expect(relationPhrase(relation({ code: 'documents' }), 'inverse', t)).toBe('Documents');
  });

  it('reads an undirected relation the same from either end', () => {
    const undirected = relation({ code: 'related-to', directed: false, inverseName: null });
    expect(relationPhrase(undirected, 'forward', t)).toBe('Related to');
    expect(relationPhrase(undirected, 'inverse', t)).toBe('Related to');
  });

  it('shows a custom relation exactly as an administrator wrote it', () => {
    const custom = relation({ code: 'sump-beyond', name: 'Sump beyond', inverseName: 'Reached through', seeded: false });
    expect(relationPhrase(custom, 'forward', t)).toBe('Sump beyond');
    expect(relationPhrase(custom, 'inverse', t)).toBe('Reached through');
  });

  it('falls back to plain wording when there is no relation at all', () => {
    expect(relationPhrase(null, 'forward', t)).toBe('Related');
  });

  it('reads a directed relation from the page it is shown on', () => {
    const link: ResLink = {
      id: 'l1',
      shortCode: 'Ab3xY9Zq',
      relationType: relation({}),
      description: null,
      createdBy: null,
      mayEdit: false,
      createdAt: '2026-08-05T00:00:00Z',
      updatedAt: '2026-08-05T00:00:00Z',
      members: [
        member({ id: 'm1', targetId: 'cave', isMain: true }),
        member({ id: 'm2', targetId: 'passage' }),
      ],
    };
    expect(relationPhraseFor(link, 'feature', 'cave', t)).toBe('Contains');
    expect(relationPhraseFor(link, 'feature', 'passage', t)).toBe('Contained in');
    // A page whose own member row this caller cannot see reads forward rather than guessing.
    expect(relationPhraseFor(link, 'feature', 'elsewhere', t)).toBe('Contains');
  });

  it('lists only directed codes as reading two ways', () => {
    for (const code of DIRECTED_RELATION_CODES) {
      expect(SEEDED_RELATION_CODES).toContain(code);
    }
    expect(DIRECTED_RELATION_CODES).toHaveLength(14);
  });

  // The picker offers exactly what the server admits: an offer it refuses is a form that
  // cannot be submitted, and one it accepts but that is missing here is a capability the
  // user never sees.
  it('admits the anchor kinds the server admits, and the whole resource for every type', () => {
    expect(admittedAnchorKinds('feature')).toEqual(['whole']);
    expect(admittedAnchorKinds('document')).toEqual([
      'whole',
      'textRange',
      'page',
      'pageRange',
      'imageRegion',
      'timePoint',
      'timeRange',
      'modelPoint',
    ]);
    expect(admittedAnchorKinds('surveyModel')).toEqual([
      'whole',
      'modelStation',
      'modelStationRange',
      'modelSurvey',
      'modelSurveyRange',
    ]);
    expect(admittedAnchorKinds('geofile')).toEqual(['whole', 'waypoint', 'waypointRange']);
    // Types with no parts, and a type this client has never heard of, all link whole.
    for (const type of [
      'tripLog',
      'caver',
      'cavingGroup',
      'mapView',
      'cabinet',
      'expedition',
      'somethingNew',
    ]) {
      expect(admittedAnchorKinds(type), type).toEqual(['whole']);
    }
    for (const type of RESLINK_TARGET_TYPES) {
      expect(admittedAnchorKinds(type)[0], type).toBe('whole');
    }
  });

  it('can compose the numeric anchors, a text selection, and the whole resource', () => {
    const composable = RESLINK_ANCHOR_KINDS.filter(canComposeAnchor).sort();
    expect(composable).toEqual(['page', 'pageRange', 'textRange', 'timePoint', 'timeRange', 'whole']);
  });

  it('mirrors the server rules for the anchors it can compose', () => {
    expect(anchorProblem('page', { page: 1 }, t)).toBeNull();
    expect(anchorProblem('page', { page: 0 }, t)).not.toBeNull();
    expect(anchorProblem('page', { page: 2.5 }, t)).not.toBeNull();
    expect(anchorProblem('page', null, t)).not.toBeNull();

    // A one-page range is legal; a backwards one is not.
    expect(anchorProblem('pageRange', { fromPage: 3, toPage: 3 }, t)).toBeNull();
    expect(anchorProblem('pageRange', { fromPage: 3, toPage: 2 }, t)).not.toBeNull();

    expect(anchorProblem('timePoint', { t: 0 }, t)).toBeNull();
    expect(anchorProblem('timePoint', { t: -1 }, t)).not.toBeNull();

    // Strictly forward: a zero-length span is what a single moment is for.
    expect(anchorProblem('timeRange', { start: 0, end: 1 }, t)).toBeNull();
    expect(anchorProblem('timeRange', { start: 5, end: 5 }, t)).not.toBeNull();

    // A text selection needs both halves: the words, and where they were found in the text the
    // server read. A quote with no offsets is a payload the server refuses.
    expect(anchorProblem('textRange', { quote: 'a passage', start: 10, end: 19 }, t)).toBeNull();
    expect(anchorProblem('textRange', { quote: 'a passage' }, t)).not.toBeNull();
    expect(anchorProblem('textRange', { quote: '', start: 10, end: 19 }, t)).not.toBeNull();
    expect(anchorProblem('textRange', { quote: 'a passage', start: 19, end: 19 }, t)).not.toBeNull();

    // A kind with no editor has no client-side rule to apply, and must not invent one.
    expect(anchorProblem('imageRegion', null, t)).toBeNull();
    expect(anchorProblem('whole', null, t)).toBeNull();
    expect(anchorProblem('somethingNew', null, t)).toBeNull();
  });
});
