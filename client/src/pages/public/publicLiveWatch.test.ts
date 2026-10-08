// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { PUBLIC_ARCHIVE_FRESH_MS } from '../../api/hooks.ts';
import type { PublicLiveTrip, PublicTripEnvelope } from '../../api/hooks.ts';
import { ApiError } from '../../api/client.ts';
import {
  linkListsPastOnly,
  PAST_ONLY_EVIDENCE_MS,
  liveTripAsEnvelope,
  publicTripView,
  shownReadEnded,
  watchedParty,
} from './publicLiveWatch.ts';

/**
 * Whose party a published page draws, decided without a page.
 *
 * <b>What is proved here is that no party is ever drawn under another's name.</b> There are three
 * things a page can show and two of them can be asked for before they are in hand; every case
 * below is one of the ways the link's own party could end up under a banner naming somebody else,
 * or a watched party could be dropped — or kept — on evidence that says no such thing.
 */

const OWN = 'aaaaaaaa-0000-0000-0000-000000000001';
const OTHER = 'aaaaaaaa-0000-0000-0000-000000000002';
const THIRD = 'aaaaaaaa-0000-0000-0000-000000000003';

const MODEL = {
  modelUrl: 'https://example.invalid/files/own.3d?sig=1',
  format: 'survex3d',
  proj4: null,
  sourceEpsg: null,
  pictures: [],
  rasterMaps: [],
} as unknown as PublicTripEnvelope['model'];

function person(ordinal: number, label: string, stationName: string | null) {
  return {
    ordinal,
    label,
    teamId: null,
    stationName,
    depthM: null,
    lastRecordedAt: '2026-09-14T09:00:00Z',
    positionRecordedAt: stationName === null ? null : '2026-09-14T09:00:00Z',
    positionOnOtherModel: false,
    in: true,
    out: false,
  };
}

function own(): PublicTripEnvelope {
  return {
    tripLogId: OWN,
    expedition: null,
    title: 'E1, the deep end',
    tripDate: '2026-09-14',
    tripDateEnd: null,
    state: 'closed',
    armedAt: '2026-09-14T06:00:00Z',
    closedAt: '2026-09-14T12:00:00Z',
    expectedReturnAt: '2026-09-14T11:00:00Z',
    positionsWithheld: false,
    model: MODEL,
    teams: [],
    participants: [person(1, 'Ana', 'own.1')],
  };
}

function row(tripLogId: string, title: string): PublicLiveTrip {
  return {
    tripLogId,
    expedition: { id: 'cccccccc-0000-0000-0000-000000000001', name: 'Summer camp' },
    title,
    tripDate: '2026-09-15',
    tripDateEnd: null,
    state: 'armed',
    armedAt: '2026-09-15T07:00:00Z',
    closedAt: null,
    expectedReturnAt: '2026-09-15T16:00:00Z',
    positionsWithheld: true,
    teams: [{ id: 'dddddddd-0000-0000-0000-000000000001', title: 'Survey' }],
    participants: [person(1, 'Mircea', 'other.4'), person(2, 'Ileana', null)],
  };
}

describe('a row of the list as the party a page draws', () => {
  it('keeps everything the row says of the party, and draws it on the link’s own survey', () => {
    const watched = liveTripAsEnvelope(row(OTHER, 'E2, the survey'), own());

    expect(watched.model).toBe(MODEL);
    expect(watched).toMatchObject({
      tripLogId: OTHER,
      title: 'E2, the survey',
      tripDate: '2026-09-15',
      state: 'armed',
      armedAt: '2026-09-15T07:00:00Z',
      closedAt: null,
      // The watched party's own plan, not the plan of the trip the link is for.
      expectedReturnAt: '2026-09-15T16:00:00Z',
      positionsWithheld: true,
      expedition: { name: 'Summer camp' },
    });
    expect(watched.teams.map((team) => team.title)).toEqual(['Survey']);
    expect(watched.participants.map((participant) => participant.label)).toEqual(['Mircea', 'Ileana']);
  });
});

describe('finding the party a reader asked to watch', () => {
  const list = { trips: [row(OWN, 'E1, the deep end'), row(OTHER, 'E2, the survey')], more: false };

  it('finds it by identifier, never by title', () => {
    const twins = { trips: [row(OWN, 'The same title'), row(OTHER, 'The same title')], more: false };

    const found = watchedParty(twins, OTHER, OWN);

    expect(found.kind).toBe('watching');
    expect(found.kind === 'watching' && found.trip.tripLogId).toBe(OTHER);
  });

  it('is nobody when nothing was asked for, and when the link’s own trip was', () => {
    expect(watchedParty(list, null, OWN)).toEqual({ kind: 'none' });
    // In the list as well, and deliberately not a watch: the page reads it by its own route.
    expect(watchedParty(list, OWN, OWN)).toEqual({ kind: 'none' });
  });

  it('does not take a list that has not been read for a party that has left', () => {
    expect(watchedParty(undefined, OTHER, OWN)).toEqual({ kind: 'waiting' });
    // The positive twin: the same question of a list that was read.
    expect(watchedParty(list, THIRD, OWN)).toEqual({ kind: 'gone' });
    expect(watchedParty({ trips: [], more: false }, OTHER, OWN)).toEqual({ kind: 'gone' });
  });

  it('ends the watch as well when the list is cut short, since nothing fresh can be drawn', () => {
    expect(watchedParty({ trips: [row(OWN, 'E1')], more: true }, OTHER, OWN)).toEqual({ kind: 'gone' });
  });
});

describe('what a published page shows', () => {
  const live = { engaged: false, envelope: null };
  const watching = watchedParty({ trips: [row(OTHER, 'E2, the survey')], more: false }, OTHER, OWN);

  it('shows the link’s own trip when nothing else was asked for', () => {
    const mine = own();

    expect(publicTripView(mine, live, { kind: 'none' })).toEqual({ mode: 'own', envelope: mine });
  });

  it('shows the watched party, on the link’s own survey', () => {
    const view = publicTripView(own(), live, watching);

    expect(view.mode).toBe('watched');
    expect(view.envelope?.title).toBe('E2, the survey');
    expect(view.envelope?.participants.map((participant) => participant.label)).toEqual([
      'Mircea',
      'Ileana',
    ]);
    expect(view.envelope?.model).toBe(MODEL);
  });

  it('lets a past trip win over a watch', () => {
    const then = { ...own(), tripLogId: THIRD, title: 'The 2019 push' };

    expect(publicTripView(own(), { engaged: true, envelope: then }, watching)).toEqual({
      mode: 'past',
      envelope: then,
    });
  });

  it('draws nobody while a chosen past trip or a watched party is not in hand yet', () => {
    // Never the link's own party in either case: it would stand under a banner naming somebody else.
    expect(publicTripView(own(), { engaged: true, envelope: null }, { kind: 'none' })).toEqual({
      mode: 'past',
      envelope: undefined,
    });
    expect(publicTripView(own(), live, { kind: 'waiting' })).toEqual({
      mode: 'watched',
      envelope: undefined,
    });
    expect(publicTripView(undefined, live, watching)).toEqual({ mode: 'watched', envelope: undefined });
  });

  it('goes back to the link’s own trip when the watched party has left the list', () => {
    const mine = own();

    expect(publicTripView(mine, live, { kind: 'gone' })).toEqual({ mode: 'own', envelope: mine });
  });
});

describe('whether the read feeding the screen has been refused for good', () => {
  const refused = new ApiError(404, 'tracking.share_not_found');
  const unreachable = new ApiError(503);

  it('asks the list, and not this link’s own read, while another party is on screen', () => {
    // The link's own trip is no longer published and the list still answers: not ended.
    expect(shownReadEnded('watched', refused, null)).toBe(false);
    // The list refused for good, whatever the link's own read last heard: ended.
    expect(shownReadEnded('watched', null, refused)).toBe(true);
    expect(shownReadEnded('watched', refused, refused)).toBe(true);
  });

  it('asks this link’s own read while its own trip or a replay is on screen', () => {
    expect(shownReadEnded('own', refused, null)).toBe(true);
    expect(shownReadEnded('own', null, refused)).toBe(false);
    expect(shownReadEnded('past', refused, null)).toBe(true);
  });

  it('never takes a read that merely did not land, or was told to wait, for a final answer', () => {
    expect(shownReadEnded('watched', null, unreachable)).toBe(false);
    expect(shownReadEnded('watched', null, new TypeError('Failed to fetch'))).toBe(false);
    expect(shownReadEnded('watched', null, new ApiError(429))).toBe(false);
    expect(shownReadEnded('own', unreachable, null)).toBe(false);
    expect(shownReadEnded('own', undefined, undefined)).toBe(false);
  });
});

describe('whether a link has stopped listing today’s parties while its past trips still answer', () => {
  const refused = new ApiError(404, 'tracking.share_not_found');
  const pastList = { trips: [], more: false };
  /** The moment the list of parties was refused for good. */
  const REFUSED_AT = 1_800_000_000_000;
  /** A past list read a moment before that refusal. */
  const past = (over: Partial<{ list: unknown; error: unknown; readAt: number }> = {}) => ({
    list: pastList,
    error: null,
    readAt: REFUSED_AT - 1_000,
    ...over,
  });
  /** A list of parties that never answered through this link and was refused for good. */
  const live = (over: Partial<{ error: unknown; readAt: number; refusedAt: number }> = {}) => ({
    error: refused,
    readAt: 0,
    refusedAt: REFUSED_AT,
    ...over,
  });

  it('says so of a past list read at about the moment the list of parties was refused for good', () => {
    expect(linkListsPastOnly(past(), live())).toBe(true);
    // Read after the refusal — the strongest form of it.
    expect(linkListsPastOnly(past({ readAt: REFUSED_AT + 60_000 }), live())).toBe(true);
    // A re-read of the past list that merely did not land leaves the list in hand as it was.
    expect(linkListsPastOnly(past({ error: new ApiError(503) }), live())).toBe(true);
    // And the positive case beside the negatives below: both lists answering is not it.
    expect(
      linkListsPastOnly(past(), live({ error: null, readAt: REFUSED_AT, refusedAt: 0 })),
    ).toBe(false);
  });

  it('does not say so of a list of parties that failed on the network, was busy or was told to wait', () => {
    expect(linkListsPastOnly(past(), live({ error: new TypeError('Failed to fetch') }))).toBe(false);
    expect(linkListsPastOnly(past(), live({ error: new ApiError(503) }))).toBe(false);
    expect(linkListsPastOnly(past(), live({ error: new ApiError(429) }))).toBe(false);
    expect(
      linkListsPastOnly(
        past(),
        live({ error: new ApiError(503, undefined, undefined, undefined, 30_000) }),
      ),
    ).toBe(false);
    expect(linkListsPastOnly(past(), live({ error: undefined }))).toBe(false);
  });

  it('does not say so when the past list is refused as well, even with an earlier list in hand', () => {
    expect(linkListsPastOnly(past({ list: undefined, error: refused }), live())).toBe(false);
    expect(linkListsPastOnly(past({ error: refused }), live())).toBe(false);
  });

  it('does not say so of a past list nobody has read, since nothing is read to find out', () => {
    expect(linkListsPastOnly(past({ list: undefined, readAt: 0 }), live())).toBe(false);
    expect(linkListsPastOnly(past({ list: undefined, error: undefined, readAt: 0 }), live())).toBe(
      false,
    );
    // A list with no moment of arrival was not read.
    expect(linkListsPastOnly(past({ readAt: 0 }), live({ refusedAt: 0 }))).toBe(false);
  });

  it('does not say so when the list of parties answered after the past list was read: a link taken back since leaves the same pair behind', () => {
    // Archive opened, then the parties followed for a while, then the link unpublished.
    const taken = live({ readAt: REFUSED_AT - 60_000, refusedAt: REFUSED_AT });
    expect(linkListsPastOnly(past({ readAt: REFUSED_AT - 120_000 }), taken)).toBe(false);
    // The twins: the past list read again and answered — at the parties' last answer, between
    // it and the refusal, or after the refusal.
    expect(linkListsPastOnly(past({ readAt: REFUSED_AT - 60_000 }), taken)).toBe(true);
    expect(linkListsPastOnly(past({ readAt: REFUSED_AT - 30_000 }), taken)).toBe(true);
    expect(linkListsPastOnly(past({ readAt: REFUSED_AT + 1 }), taken)).toBe(true);
  });

  it('does not say so of a past list read longer before the refusal than it is believed for', () => {
    expect(linkListsPastOnly(past({ readAt: REFUSED_AT - PAST_ONLY_EVIDENCE_MS }), live())).toBe(
      true,
    );
    expect(
      linkListsPastOnly(past({ readAt: REFUSED_AT - PAST_ONLY_EVIDENCE_MS - 1 }), live()),
    ).toBe(false);
  });

  it('believes a past list for exactly as long as a returning reader is shown it without a re-read', () => {
    expect(PAST_ONLY_EVIDENCE_MS).toBe(PUBLIC_ARCHIVE_FRESH_MS);
  });
});
