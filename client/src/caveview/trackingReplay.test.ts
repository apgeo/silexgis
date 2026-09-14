// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { TrackingEvent, TrackingState } from '../api/hooks.ts';
import {
  noteAfter,
  noteAt,
  noteBefore,
  replayNotes,
  replayWindow,
  trackedCaversAt,
} from './trackingReplay.ts';

const MODEL = 'model-1';
const OTHER_MODEL = 'model-2';
const ANA = 'caver-ana';
const BOGDAN = 'caver-bogdan';

const at = (iso: string) => Date.parse(iso);

function state(overrides: Partial<TrackingState> = {}): TrackingState {
  return {
    state: 'armed',
    surveyModelId: MODEL,
    referenceStationName: null,
    depthFilter: [],
    armedAt: '2026-09-12T08:00:00Z',
    closedAt: null,
    positionsWithheld: false,
    teams: [
      { id: 'team-1', title: 'Echipa 1' },
      { id: 'team-2', title: 'Echipa 2' },
    ],
    participants: [
      {
        caverId: ANA,
        // Null because the logs these tests drive carry no team on any report, and this field is
        // not a roster field: see `teamIdOf`. Hand-setting it to a team no report names would be a
        // watch the server cannot send, and an assertion made against one proves nothing.
        teamId: null,
        lastKind: 'atStation',
        lastRecordedAt: '2026-09-12T10:00:00Z',
        stationName: 'p.g.9',
        depthM: null,
        out: false,
      },
    ],
    ...overrides,
  } as TrackingState;
}

/**
 * The watch's own team label for a caver, folded exactly as the server folds it: the team named on
 * that caver's <b>latest</b> team-bearing report over the whole log.
 *
 * Written out here rather than set by hand so these fixtures cannot drift into a shape the server
 * has no way of producing — which is the only shape in which a label taken from the watch rather
 * than from the log at the moment being replayed looks harmless.
 */
const teamIdOf = (log: readonly TrackingEvent[], caverId: string): string | null =>
  [...log]
    .sort((left, right) => Date.parse(left.recordedAt) - Date.parse(right.recordedAt))
    .reduce<string | null>(
      (found, event) => (event.caverId === caverId ? (event.teamId ?? found) : found),
      null,
    );

let sequence = 0;
function event(overrides: Partial<TrackingEvent> & { recordedAt: string }): TrackingEvent {
  return {
    id: `event-${sequence++}`,
    caverId: ANA,
    teamId: null,
    kind: 'atStation',
    surveyModelId: MODEL,
    stationName: null,
    depthEnteredM: null,
    note: null,
    ...overrides,
  } as TrackingEvent;
}

/** The server lists reports newest first, and so does everything that hands them over. */
const newestFirst = (events: TrackingEvent[]) =>
  [...events].sort((left, right) => Date.parse(right.recordedAt) - Date.parse(left.recordedAt));

const nameOf = (caverId: string) =>
  caverId === ANA ? 'Ana' : caverId === BOGDAN ? 'Bogdan' : 'Somebody not on the roster';

describe('trackedCaversAt', () => {
  it('places somebody at the latest report that claimed a place, at or before the moment', () => {
    const log = newestFirst([
      event({ recordedAt: '2026-09-12T08:10:00Z', kind: 'entered' }),
      event({ recordedAt: '2026-09-12T08:40:00Z', stationName: 'p.g.3' }),
      event({ recordedAt: '2026-09-12T09:20:00Z', stationName: 'p.g.9' }),
    ]);

    // Before anybody said anything, everybody on the watch is listed and nobody is placed.
    expect(trackedCaversAt(state(), log, at('2026-09-12T08:05:00Z'), nameOf, MODEL)).toEqual([
      {
        caverId: ANA,
        name: 'Ana',
        // The team a report carried at the moment being replayed, and nothing else — so before
        // any report has named one there is no team, which is what was known then.
        teamId: null,
        teamTitle: null,
        position: { kind: 'unreported' },
        lastRecordedAt: null,
        positionAt: null,
        enteredAt: null,
        out: false,
      },
    ]);

    const early = trackedCaversAt(state(), log, at('2026-09-12T09:00:00Z'), nameOf, MODEL);
    expect(early[0].position).toEqual({ kind: 'station', station: 'p.g.3' });
    expect(early[0].enteredAt).toBe('2026-09-12T08:10:00Z');
    expect(early[0].lastRecordedAt).toBe('2026-09-12T08:40:00Z');
    expect(early[0].positionAt).toBe('2026-09-12T08:40:00Z');

    // The moment the later report was made counts as being at or before it.
    const later = trackedCaversAt(state(), log, at('2026-09-12T09:20:00Z'), nameOf, MODEL);
    expect(later[0].position).toEqual({ kind: 'station', station: 'p.g.9' });
  });

  /**
   * The rule this module may not soften. A station report always carries a place; one arriving
   * without a place was kept from this reader and can be nothing else. Nobody is drawn at it —
   * there is nowhere to put a marker — and they are still listed, as withheld, because leaving them
   * out would turn a withholding into nobody knowing where they are.
   */
  it('says a position that was withheld was withheld, and invents no marker for it', () => {
    const log = newestFirst([
      event({ recordedAt: '2026-09-12T08:40:00Z', stationName: 'p.g.3' }),
      event({ recordedAt: '2026-09-12T09:10:00Z', stationName: null }),
    ]);
    const withheld = state({ positionsWithheld: true });

    expect(
      trackedCaversAt(withheld, log, at('2026-09-12T09:30:00Z'), nameOf, MODEL)[0].position,
    ).toEqual({ kind: 'withheld', certain: true });

    // A depth report is the same kind of claim and reads the same way when its place is removed.
    const depth = newestFirst([
      event({ recordedAt: '2026-09-12T09:10:00Z', kind: 'atDepth', depthEnteredM: null }),
    ]);
    expect(
      trackedCaversAt(withheld, depth, at('2026-09-12T09:30:00Z'), nameOf, MODEL)[0].position,
    ).toEqual({ kind: 'withheld', certain: true });

    // And a depth that did arrive is said as a depth rather than placed at a station.
    const told = newestFirst([
      event({ recordedAt: '2026-09-12T09:10:00Z', kind: 'atDepth', depthEnteredM: 35 }),
    ]);
    expect(
      trackedCaversAt(state(), told, at('2026-09-12T09:30:00Z'), nameOf, MODEL)[0].position,
    ).toEqual({ kind: 'depth', depthM: 35 });
  });

  it('claims no withholding where nothing is being withheld', () => {
    // Said something, but nothing that claims a place, and no position is being kept from anybody.
    const spoke = newestFirst([
      event({ recordedAt: '2026-09-12T08:10:00Z', kind: 'entered' }),
      event({ recordedAt: '2026-09-12T08:30:00Z', kind: 'note', note: 'radio check' }),
    ]);
    expect(
      trackedCaversAt(state(), spoke, at('2026-09-12T09:00:00Z'), nameOf, MODEL)[0].position,
    ).toEqual({ kind: 'unreported' });

    // The same log where positions are being withheld cannot tell a hidden position from an
    // unreported one, and says so rather than claiming either.
    expect(
      trackedCaversAt(
        state({ positionsWithheld: true }),
        spoke,
        at('2026-09-12T09:00:00Z'),
        nameOf,
        MODEL,
      )[0].position,
    ).toEqual({ kind: 'withheld', certain: false });

    // Nobody has said anything at all: there is no position to keep from anybody, so saying
    // "withheld" here would invent a secret.
    expect(
      trackedCaversAt(
        state({ positionsWithheld: true }),
        spoke,
        at('2026-09-12T08:00:00Z'),
        nameOf,
        MODEL,
      )[0].position,
    ).toEqual({ kind: 'unreported' });
  });

  it('shows somebody who has come out as out, where they were last reported', () => {
    const log = newestFirst([
      event({ recordedAt: '2026-09-12T08:10:00Z', kind: 'entered' }),
      event({ recordedAt: '2026-09-12T08:40:00Z', stationName: 'p.g.3' }),
      event({ recordedAt: '2026-09-12T11:00:00Z', kind: 'exited' }),
    ]);

    const inside = trackedCaversAt(state(), log, at('2026-09-12T10:00:00Z'), nameOf, MODEL)[0];
    expect(inside.out).toBe(false);

    const after = trackedCaversAt(state(), log, at('2026-09-12T11:30:00Z'), nameOf, MODEL)[0];
    // Still on the watch, still at the last place anybody reported — that is where they were, not
    // where they are, and taking the marker off would read as a caver who vanished.
    expect(after.out).toBe(true);
    expect(after.position).toEqual({ kind: 'station', station: 'p.g.3' });
    expect(after.lastRecordedAt).toBe('2026-09-12T11:00:00Z');

    // Coming out and going back in puts somebody back underground, on the strength of the later
    // entry, and the moment they went in is the later one.
    const again = newestFirst([
      ...log,
      event({ recordedAt: '2026-09-12T12:00:00Z', kind: 'entered' }),
    ]);
    const back = trackedCaversAt(state(), again, at('2026-09-12T12:30:00Z'), nameOf, MODEL)[0];
    expect(back.out).toBe(false);
    expect(back.enteredAt).toBe('2026-09-12T12:00:00Z');
  });

  it('lets a note say something happened without saying where', () => {
    const log = newestFirst([
      event({ recordedAt: '2026-09-12T08:40:00Z', stationName: 'p.g.3' }),
      event({ recordedAt: '2026-09-12T09:10:00Z', kind: 'note', note: 'water rising' }),
    ]);

    const caver = trackedCaversAt(state(), log, at('2026-09-12T09:30:00Z'), nameOf, MODEL)[0];
    // The note is the latest report and the station is the latest *place*: the two deliberately
    // disagree, and folding them together would move somebody back to nowhere because their last
    // word was a radio check.
    expect(caver.position).toEqual({ kind: 'station', station: 'p.g.3' });
    expect(caver.lastRecordedAt).toBe('2026-09-12T09:10:00Z');
    // And the position keeps the age of the report that placed it, which is the half the folded
    // watch cannot carry: a replay reads the very report, so it always knows how old a station is,
    // and anything comparing two people's positions — where a team is standing, say — needs that
    // rather than the moment somebody last said anything.
    expect(caver.positionAt).toBe('2026-09-12T08:40:00Z');
  });

  it('draws nobody from a report measured in another survey', () => {
    const log = newestFirst([
      event({ recordedAt: '2026-09-12T08:40:00Z', stationName: 'p.g.3' }),
      event({
        recordedAt: '2026-09-12T09:10:00Z',
        stationName: 'sala-mare.4',
        surveyModelId: OTHER_MODEL,
      }),
    ]);

    // The later report names a place in another cave. It is passed over rather than drawn here.
    expect(
      trackedCaversAt(state(), log, at('2026-09-12T09:30:00Z'), nameOf, MODEL)[0].position,
    ).toEqual({ kind: 'station', station: 'p.g.3' });

    // And a watch resolved against another model draws nobody at all, exactly as the live path.
    expect(trackedCaversAt(state({ surveyModelId: OTHER_MODEL }), log, 0, nameOf, MODEL)).toEqual([]);
    expect(trackedCaversAt(state({ surveyModelId: null }), log, 0, nameOf, MODEL)).toEqual([]);
    expect(trackedCaversAt(state(), log, 0, nameOf, undefined)).toEqual([]);
  });

  it('names every caver on the watch at every moment, whatever the log says', () => {
    const watch = state({
      participants: [
        ...state().participants,
        {
          caverId: BOGDAN,
          teamId: null,
          lastKind: null,
          lastRecordedAt: null,
          stationName: null,
          depthM: null,
          out: false,
          label: null,
        },
      ],
    });
    const log = newestFirst([event({ recordedAt: '2026-09-12T08:40:00Z', stationName: 'p.g.3' })]);

    const cavers = trackedCaversAt(watch, log, at('2026-09-12T09:00:00Z'), nameOf, MODEL);
    // The list is the watch's, in the watch's order: a replay whose list grew and shrank as it
    // played would be a different surface from the live one it stands in for.
    expect(cavers.map((caver) => caver.caverId)).toEqual([ANA, BOGDAN]);
    expect(cavers[1]).toMatchObject({ name: 'Bogdan', position: { kind: 'unreported' } });
  });

  it('labels somebody with the team their report carried at the time, and never a later one', () => {
    const log = newestFirst([
      event({ recordedAt: '2026-09-12T08:10:00Z', kind: 'entered' }),
      event({ recordedAt: '2026-09-12T09:00:00Z', stationName: 'p.g.3', teamId: 'team-1' }),
      event({ recordedAt: '2026-09-12T11:00:00Z', stationName: 'p.g.9', teamId: 'team-2' }),
    ]);
    // The watch this is read beside, built the way the server builds it: Ana's label is the team
    // of her latest team-bearing report, so it says the team she is moved into at eleven.
    const participants = [{ ...state().participants[0], teamId: teamIdOf(log, ANA) }];
    const watch = state({ participants });
    expect(participants[0].teamId).toBe('team-2');

    // Half past eight: Ana has gone in and no report has named a team yet, so nothing knew of one.
    // The watch's own label is the only other answer available and it is the team she is put in
    // two and a half hours later — the label a replay may never show.
    expect(
      trackedCaversAt(watch, log, at('2026-09-12T08:30:00Z'), nameOf, MODEL)[0].teamTitle,
    ).toBeNull();
    expect(
      trackedCaversAt(watch, log, at('2026-09-12T10:00:00Z'), nameOf, MODEL)[0].teamTitle,
    ).toBe('Echipa 1');
    // And at the end of the log the replay and the live watch agree, because there the watch's
    // fold and the report in force are the same report.
    expect(
      trackedCaversAt(watch, log, at('2026-09-12T11:30:00Z'), nameOf, MODEL)[0].teamTitle,
    ).toBe('Echipa 2');

    // A team deleted since loses its title rather than showing an id, as on the live path.
    expect(
      trackedCaversAt(
        state({ participants, teams: [] }),
        log,
        at('2026-09-12T11:30:00Z'),
        nameOf,
        MODEL,
      )[0].teamTitle,
    ).toBeNull();
  });

  it('leaves out a report whose time cannot be read rather than placing it anywhere', () => {
    const log = newestFirst([event({ recordedAt: '2026-09-12T08:40:00Z', stationName: 'p.g.3' })]);
    const broken = [{ ...log[0], id: 'broken', recordedAt: 'not a time', stationName: 'p.g.99' }];

    expect(
      trackedCaversAt(state(), [...broken, ...log], at('2026-09-12T09:00:00Z'), nameOf, MODEL)[0]
        .position,
    ).toEqual({ kind: 'station', station: 'p.g.3' });
  });
});

describe('replayWindow', () => {
  const events = newestFirst([
    event({ recordedAt: '2026-09-12T08:40:00Z', stationName: 'p.g.3' }),
  ]);

  it('spans a closed watch from when it was armed to when it was closed', () => {
    const window = replayWindow(
      { armedAt: '2026-09-12T08:00:00Z', closedAt: '2026-09-12T12:00:00Z' },
      events,
      at('2026-09-13T00:00:00Z'),
    );
    expect(window).toEqual({ from: at('2026-09-12T08:00:00Z'), to: at('2026-09-12T12:00:00Z') });
  });

  it('spans a live watch up to the moment the replay was opened', () => {
    const window = replayWindow(
      { armedAt: '2026-09-12T08:00:00Z', closedAt: null },
      events,
      at('2026-09-12T10:30:00Z'),
    );
    expect(window).toEqual({ from: at('2026-09-12T08:00:00Z'), to: at('2026-09-12T10:30:00Z') });
  });

  it('widens to hold a report stamped outside it', () => {
    // A report can be stamped with when it was said rather than when it was written down, so word
    // relayed out of the cave lands where the watch was not yet armed. A scrubber that could not be
    // dragged to a report is a replay with a report missing from it.
    const window = replayWindow(
      { armedAt: '2026-09-12T08:00:00Z', closedAt: '2026-09-12T09:00:00Z' },
      newestFirst([
        event({ recordedAt: '2026-09-12T07:30:00Z', kind: 'entered' }),
        event({ recordedAt: '2026-09-12T09:45:00Z', kind: 'exited' }),
      ]),
      at('2026-09-12T09:10:00Z'),
    );
    expect(window).toEqual({ from: at('2026-09-12T07:30:00Z'), to: at('2026-09-12T09:45:00Z') });
  });

  it('offers nothing to scrub where there is no stretch of time', () => {
    expect(replayWindow({ armedAt: null, closedAt: null }, events, Date.now())).toBeNull();
    expect(
      replayWindow(
        { armedAt: '2026-09-12T08:00:00Z', closedAt: '2026-09-12T08:00:00Z' },
        [],
        at('2026-09-12T09:00:00Z'),
      ),
    ).toBeNull();
  });
});

describe('the notes a replay can land on', () => {
  const window = { from: at('2026-09-12T08:00:00Z'), to: at('2026-09-12T12:00:00Z') };
  const log = newestFirst([
    event({ recordedAt: '2026-09-12T08:30:00Z', kind: 'note', note: 'radio check' }),
    event({ recordedAt: '2026-09-12T09:30:00Z', stationName: 'p.g.3', note: 'rigging the pitch' }),
    event({ recordedAt: '2026-09-12T10:30:00Z', kind: 'note', note: null }),
    event({ recordedAt: '2026-09-12T13:00:00Z', kind: 'note', note: 'outside the window' }),
  ]);

  it('is every report that said something in words, oldest first', () => {
    // Any report can carry words — the form offers the field whatever is being reported — so this
    // is not the note kind alone, and a note field left empty is not a mark.
    expect(replayNotes(log, window)).toEqual([
      { at: at('2026-09-12T08:30:00Z'), caverId: ANA, kind: 'note', note: 'radio check' },
      { at: at('2026-09-12T09:30:00Z'), caverId: ANA, kind: 'atStation', note: 'rigging the pitch' },
    ]);
  });

  it('lands on the note in force, and steps to the ones either side of it', () => {
    const notes = replayNotes(log, window);
    expect(noteAt(notes, at('2026-09-12T08:00:00Z'))).toBeNull();
    expect(noteAt(notes, at('2026-09-12T09:00:00Z'))?.note).toBe('radio check');
    // Standing exactly on a note is standing on it, not before it.
    expect(noteAt(notes, at('2026-09-12T09:30:00Z'))?.note).toBe('rigging the pitch');

    expect(noteBefore(notes, at('2026-09-12T09:30:00Z'))?.note).toBe('radio check');
    expect(noteBefore(notes, at('2026-09-12T08:30:00Z'))).toBeNull();
    expect(noteAfter(notes, at('2026-09-12T08:30:00Z'))?.note).toBe('rigging the pitch');
    expect(noteAfter(notes, at('2026-09-12T09:30:00Z'))).toBeNull();
  });
});
