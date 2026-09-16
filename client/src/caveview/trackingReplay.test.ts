// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { ResLink, ResLinkMember, TrackingEvent, TrackingState } from '../api/hooks.ts';
import {
  noteAfter,
  noteAt,
  noteBefore,
  picturesAt,
  placedPicturesAt,
  replayNotes,
  replayPictures,
  replayWindow,
  trackedCaversAt,
  type ReplayPicture,
} from './trackingReplay.ts';

const MODEL = 'model-1';
const TRIP = 'trip-1';
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
    publishesRealNames: true,
    surveyModelMissing: false,
    participants: [
      {
        caverId: ANA,
        // Null because the logs these tests drive carry no team on any report, and this field is
        // not a roster field: see `teamIdOf`. Hand-setting it to a team no report names would be a
        // watch the server cannot send, and an assertion made against one proves nothing.
        teamId: null,
        lastKind: 'atStation',
        lastRecordedAt: '2026-09-12T10:00:00Z',
        positionRecordedAt: '2026-09-12T10:00:00Z',
        stationName: 'p.g.9',
        depthM: null,
        // The watch's own fold, which this module never reads — it replays the log — but which a
        // real answer always carries. Written out because the cast that used to stand here let a
        // required field go missing from a fixture and take a whole party off the model.
        positionSurveyModelId: MODEL,
        label: null,
        in: true,
        out: false,
      },
    ],
    ...overrides,
  };
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

  it('says a report measured in another survey rather than drawing it or passing it over', () => {
    const log = newestFirst([
      event({ recordedAt: '2026-09-12T08:40:00Z', stationName: 'p.g.3' }),
      event({
        recordedAt: '2026-09-12T09:10:00Z',
        stationName: 'sala-mare.4',
        surveyModelId: OTHER_MODEL,
      }),
    ]);

    // Before the second report, the first one is drawn: the rule is about which survey a place was
    // measured in, not a refusal to place anybody once a survey has been changed.
    expect(
      trackedCaversAt(state(), log, at('2026-09-12T09:00:00Z'), nameOf, MODEL)[0].position,
    ).toEqual({ kind: 'station', station: 'p.g.3' });

    // After it, the latest word about this person is a place in another survey. It takes effect as
    // a place that cannot be shown here — never as the older station, which would draw somebody
    // where they were an hour ago as if nothing newer had been heard.
    const later = trackedCaversAt(state(), log, at('2026-09-12T09:30:00Z'), nameOf, MODEL)[0];
    expect(later.position).toEqual({ kind: 'otherModel' });
    expect(later.position).not.toEqual({ kind: 'unreported' });

    // The live fold and the replay now agree, which is the whole point of the change: a watch
    // re-pointed at a corrected survey used to make the replay refuse a report the live view drew.
    expect(
      trackedCaversAt(
        state({ surveyModelId: OTHER_MODEL }),
        newestFirst([
          event({ recordedAt: '2026-09-12T08:40:00Z', stationName: 'p.g.3' }),
        ]),
        at('2026-09-12T09:30:00Z'),
        nameOf,
        MODEL,
      )[0].position,
    ).toEqual({ kind: 'station', station: 'p.g.3' });

    // A panel that does not know its own survey can compare nothing and places nobody.
    expect(trackedCaversAt(state(), log, 0, nameOf, undefined)).toEqual([]);
  });

  /**
   * The shape a deleted survey leaves on the log, which used to be read as a withholding and drawn
   * anyway.
   *
   * Removing a survey model nulls the model on every report that named it and leaves the station
   * name exactly where it was, so "no model, but a station" is not a corrupt row and not a
   * withholding — it is the ordinary record of a place measured in a survey that is no longer
   * here. Drawn on whatever survey the watch was pointed at afterwards, it puts a person at
   * whichever node of the new geometry happens to carry that name.
   */
  it('refuses to draw a station whose survey has been deleted, and still says it exists', () => {
    const gone = newestFirst([
      event({ recordedAt: '2026-09-12T08:40:00Z', stationName: 'p.g.3', surveyModelId: null }),
    ]);
    const caver = trackedCaversAt(state(), gone, at('2026-09-12T09:30:00Z'), nameOf, MODEL)[0];
    expect(caver.position).toEqual({ kind: 'otherModel' });
    // Never as the station itself, which is the marker this repairs, and never as an absence,
    // which would say nobody had reported where this person is.
    expect(caver.position).not.toEqual({ kind: 'station', station: 'p.g.3' });
    expect(caver.position).not.toEqual({ kind: 'unreported' });

    // The twin, so this is a rule about the survey and not about station reports in general: the
    // same report naming the survey on screen is still drawn at its station.
    const kept = newestFirst([
      event({ recordedAt: '2026-09-12T08:40:00Z', stationName: 'p.g.3', surveyModelId: MODEL }),
    ]);
    expect(
      trackedCaversAt(state(), kept, at('2026-09-12T09:30:00Z'), nameOf, MODEL)[0].position,
    ).toEqual({ kind: 'station', station: 'p.g.3' });

    // And the withholding keeps its own reading: a station report stripped of everything — its
    // station, its depth and its model — is a position this reader may not be told, which is a
    // different sentence from a place that cannot be drawn here.
    const withheld = newestFirst([
      event({
        recordedAt: '2026-09-12T08:40:00Z',
        stationName: null,
        surveyModelId: null,
      }),
    ]);
    expect(
      trackedCaversAt(
        state({ positionsWithheld: true }),
        withheld,
        at('2026-09-12T09:30:00Z'),
        nameOf,
        MODEL,
      )[0].position,
    ).toEqual({ kind: 'withheld', certain: true });
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
          positionRecordedAt: null,
          stationName: null,
          depthM: null,
          positionSurveyModelId: null,
          in: false,
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

describe('replayPictures', () => {
  const PICTURE = 'doc-picture';
  const OTHER_PICTURE = 'doc-other';

  function member(overrides: Partial<ResLinkMember>): ResLinkMember {
    return {
      id: `member-${sequence++}`,
      targetType: 'document',
      targetId: PICTURE,
      isMain: false,
      sortOrder: 0,
      note: null,
      anchorKind: 'whole',
      anchor: null,
      anchorFileId: null,
      anchorState: 'exact',
      display: null,
      ...overrides,
    } as ResLinkMember;
  }

  /** A moment member: the trip, anchored to an instant — the shape the write actually stores. */
  const moment = (iso: string, tripLogId = TRIP) =>
    member({
      targetType: 'tripLog',
      targetId: tripLogId,
      isMain: true,
      anchorKind: 'tripMoment',
      anchor: { at: iso } as unknown as ResLinkMember['anchor'],
    });

  /** A photograph the caller may read: a display the server was willing to fill in. */
  const picture = (documentId = PICTURE) =>
    member({
      targetType: 'document',
      targetId: documentId,
      display: {
        title: 'At the pitch head',
        subtitle: null,
        route: null,
        thumbnailUrl: `/api/v1/files/${documentId}/thumbnail?size=480&token=abc`,
        path: null,
        mediaType: 'image/jpeg',
      } as unknown as ResLinkMember['display'],
    });

  const subject = (caverId: string) => member({ targetType: 'caver', targetId: caverId });

  const link = (members: ResLinkMember[]): ResLink =>
    ({
      id: `link-${sequence++}`,
      shortCode: 'abcd1234',
      relationType: null,
      description: null,
      createdBy: null,
      createdAt: '2026-09-12T18:00:00Z',
      updatedAt: '2026-09-12T18:00:00Z',
      mayEdit: true,
      members,
    }) as ResLink;

  it('reads a photograph off the moment and the caver its link names', () => {
    const pictures = replayPictures(
      [link([moment('2026-09-12T09:05:00Z'), subject(ANA), picture()])],
      TRIP,
    );

    expect(pictures).toHaveLength(1);
    expect(pictures[0].at).toBe(at('2026-09-12T09:05:00Z'));
    expect(pictures[0].caverId).toBe(ANA);
    expect(pictures[0].documentId).toBe(PICTURE);
    // Both widths come off the one signed URL the server minted; nothing reaches for an original.
    expect(pictures[0].entry.url).toContain('token=abc');
    expect(pictures[0].entry.thumbnailUrl).toContain('token=abc');
  });

  /**
   * A link that mentions a moment of this trip while being about something else is not a picture
   * on this trip's moment. Anyone who may read two things may relate them through the general link
   * route and choose the subject, and such a link is curated by whoever may write <em>that</em>
   * subject: this trip's write path will not extend it and its detach route will not touch it. Read
   * here it would appear on the trip's own strip with a control beside it that is refused.
   */
  it('ignores a link that merely mentions a moment while being about something else', () => {
    const mentioned = member({
      targetType: 'tripLog',
      targetId: TRIP,
      isMain: false,
      anchorKind: 'tripMoment',
      anchor: { at: '2026-09-12T09:05:00Z' } as unknown as ResLinkMember['anchor'],
    });
    expect(replayPictures([link([mentioned, picture()])], TRIP)).toEqual([]);

    // The positive twin: the same instant, the same photograph, in the shape this feature writes.
    expect(
      replayPictures([link([moment('2026-09-12T09:05:00Z'), picture()])], TRIP),
    ).toHaveLength(1);
  });

  it('ignores a moment of another trip, and a photograph the caller may not read', () => {
    // Negative half: a link naming a different trip's moment, and a member with no display at all
    // — which is exactly the shape a withheld photograph arrives in.
    expect(
      replayPictures([link([moment('2026-09-12T09:05:00Z', 'another-trip'), picture()])], TRIP),
    ).toEqual([]);
    expect(
      replayPictures(
        [link([moment('2026-09-12T09:05:00Z'), member({ targetType: 'document', display: null })])],
        TRIP,
      ),
    ).toEqual([]);

    // Positive half, so neither refusal above is a derivation that finds nothing at all.
    expect(replayPictures([link([moment('2026-09-12T09:05:00Z'), picture()])], TRIP)).toHaveLength(1);
  });

  it('shows the same photograph once however many links put it on one moment', () => {
    const pictures = replayPictures(
      [
        link([moment('2026-09-12T09:05:00Z'), picture()]),
        link([moment('2026-09-12T09:05:00Z'), picture()]),
        link([moment('2026-09-12T09:05:00Z'), picture(OTHER_PICTURE)]),
      ],
      TRIP,
    );
    expect(pictures.map((p) => p.documentId)).toEqual([PICTURE, OTHER_PICTURE]);
  });

  it('orders them oldest first, whatever order the links arrived in', () => {
    const pictures = replayPictures(
      [
        link([moment('2026-09-12T11:00:00Z'), picture(OTHER_PICTURE)]),
        link([moment('2026-09-12T09:00:00Z'), picture()]),
      ],
      TRIP,
    );
    expect(pictures.map((p) => p.at)).toEqual([
      at('2026-09-12T09:00:00Z'),
      at('2026-09-12T11:00:00Z'),
    ]);
  });

  it('names no caver when the link names two, because neither of them is the answer', () => {
    const pictures = replayPictures(
      [link([moment('2026-09-12T09:05:00Z'), subject(ANA), subject(BOGDAN), picture()])],
      TRIP,
    );
    expect(pictures[0].caverId).toBeNull();
  });
});

describe('picturesAt and where a picture is drawn', () => {
  const pictures: ReplayPicture[] = [
    {
      at: at('2026-09-12T09:00:00Z'),
      caverId: ANA,
      documentId: 'doc-1',
      memberId: 'member-1',
      entry: { url: 'u1', thumbnailUrl: 't1' },
    },
    {
      at: at('2026-09-12T09:00:00Z'),
      caverId: ANA,
      documentId: 'doc-2',
      memberId: 'member-2',
      entry: { url: 'u2', thumbnailUrl: 't2' },
    },
    {
      at: at('2026-09-12T11:00:00Z'),
      caverId: null,
      documentId: 'doc-3',
      memberId: 'member-3',
      entry: { url: 'u3', thumbnailUrl: 't3' },
    },
  ];

  /** Ana at the pitch head at nine, and in the sump an hour and a half later. */
  const log = newestFirst([
    event({ recordedAt: '2026-09-12T09:00:00Z', stationName: 'cave.upper.2' }),
    event({ recordedAt: '2026-09-12T10:30:00Z', stationName: 'cave.sump.1' }),
  ]);

  it('holds the pictures of the latest moment at or before the instant', () => {
    // The scrubber stops at a thousand places across hours and never lands on a camera's instant,
    // so "in force" is what makes a picture reachable at all — the same reading a note has.
    expect(picturesAt(pictures, at('2026-09-12T08:00:00Z'))).toEqual([]);
    expect(picturesAt(pictures, at('2026-09-12T09:30:00Z')).map((p) => p.documentId)).toEqual([
      'doc-1',
      'doc-2',
    ]);
    expect(picturesAt(pictures, at('2026-09-12T09:00:00Z'))).toHaveLength(2);
    expect(picturesAt(pictures, at('2026-09-12T12:00:00Z')).map((p) => p.documentId)).toEqual([
      'doc-3',
    ]);
  });

  it('places a picture about somebody and never one about nobody in particular', () => {
    // Positive half: a picture naming a caver is drawn at the station that caver was reported at.
    const placed = placedPicturesAt(pictures, log, at('2026-09-12T09:30:00Z'), MODEL);
    expect([...placed.keys()]).toEqual(['cave.upper.2']);
    expect(placed.get('cave.upper.2')).toHaveLength(2);

    // Negative half, and the rule that matters: a party that has split is in two places, so a
    // picture about nobody in particular is never drawn on the model at all.
    expect(placedPicturesAt(pictures, log, at('2026-09-12T12:00:00Z'), MODEL).size).toBe(0);
    expect(picturesAt(pictures, at('2026-09-12T12:00:00Z'))).toHaveLength(1);
  });

  /**
   * <b>The rule this function exists to get right.</b> A picture stays in force until the next one
   * — it has to, or a scrubber stopping at a thousand places across a day would never land on a
   * camera's instant — so at half past ten the picture in force is still the one taken at nine.
   * Folding the log at half past ten to place it draws a photograph of the pitch head in the sump,
   * and goes on moving it down the cave as the replay plays, with exactly the confidence of a
   * picture that is in the right place.
   */
  it('draws a picture where its subject was when it was taken, not where they are now', () => {
    // Ana is in the sump at this instant — the party's own marker is there, and the test below
    // asserts it, so this is a claim about the fold rather than about a fixture that never moved.
    expect(
      trackedCaversAt(state(), log, at('2026-09-12T10:45:00Z'), nameOf, MODEL)[0].position,
    ).toEqual({ kind: 'station', station: 'cave.sump.1' });

    const placed = placedPicturesAt(pictures, log, at('2026-09-12T10:45:00Z'), MODEL);
    expect([...placed.keys()]).toEqual(['cave.upper.2']);
    expect(placed.get('cave.sump.1')).toBeUndefined();
  });

  /**
   * A reader who may not place the cave is refused every position on the watch, and a photograph
   * must not hand one back by appearing under a station. The decision is here rather than on the
   * panel so it is one line with one test rather than a condition a refactor can quietly drop.
   */
  it('draws no picture for a position this reader was not told', () => {
    // A station report stripped of its place is a withholding and can be nothing else.
    const withheld = newestFirst([
      event({ recordedAt: '2026-09-12T09:00:00Z', stationName: null, surveyModelId: null }),
    ]);
    expect(placedPicturesAt(pictures, withheld, at('2026-09-12T09:30:00Z'), MODEL).size).toBe(0);
    // …and the picture is still on the trip, on the strip that says a time and no place.
    expect(picturesAt(pictures, at('2026-09-12T09:30:00Z'))).toHaveLength(2);

    // The positive twin: the same picture, the same instant, the same caver — with the place this
    // reader was allowed to be told.
    expect([...placedPicturesAt(pictures, log, at('2026-09-12T09:30:00Z'), MODEL).keys()]).toEqual([
      'cave.upper.2',
    ]);

    // And two more absences with the same twin: nobody had reported this person yet when the
    // camera fired, and a panel that does not know which survey it is drawing.
    expect(placedPicturesAt(pictures, [], at('2026-09-12T09:30:00Z'), MODEL).size).toBe(0);
    expect(placedPicturesAt(pictures, log, at('2026-09-12T09:30:00Z'), undefined).size).toBe(0);
  });

  it('widens the window so a picture off the end of the log can still be reached', () => {
    const reports = newestFirst([
      event({ recordedAt: '2026-09-12T09:00:00Z', stationName: 'p.g.3' }),
    ]);
    const late: ReplayPicture[] = [
      {
        at: at('2026-09-12T23:30:00Z'),
        caverId: ANA,
        documentId: 'doc-late',
        memberId: 'member-late',
        entry: { url: 'u', thumbnailUrl: 't' },
      },
    ];
    const tracking = state({ armedAt: '2026-09-12T08:00:00Z', closedAt: '2026-09-12T12:00:00Z' });

    // Without the pictures the window ends when the watch was closed — which is the twin that
    // makes the widening below a decision rather than an accident.
    expect(replayWindow(tracking, reports, at('2026-09-12T12:00:00Z'))?.to).toBe(
      at('2026-09-12T12:00:00Z'),
    );
    expect(replayWindow(tracking, reports, at('2026-09-12T12:00:00Z'), late)?.to).toBe(
      at('2026-09-12T23:30:00Z'),
    );
  });

  /**
   * <b>And no further, which is what keeps one dead camera battery from destroying the replay.</b>
   * A camera whose clock was never set reports 1970 or 2000, and the server accepts it on purpose
   * — refusing would throw away the record of a photograph over a number somebody can correct
   * afterwards. Widened without a bound, one such file makes the window decades long: the handle
   * then moves in steps of weeks and every real report collapses onto one end of the rail, with
   * nothing on screen to say why the trip can no longer be replayed at all.
   */
  it('refuses to be stretched by a camera clock that was never set', () => {
    const reports = newestFirst([
      event({ recordedAt: '2026-09-12T09:00:00Z', stationName: 'p.g.3' }),
    ]);
    const tracking = state({ armedAt: '2026-09-12T08:00:00Z', closedAt: '2026-09-12T12:00:00Z' });
    const picture = (iso: string): ReplayPicture[] => [
      {
        at: at(iso),
        caverId: ANA,
        documentId: 'doc-clock',
        memberId: 'member-clock',
        entry: { url: 'u', thumbnailUrl: 't' },
      },
    ];

    const ancient = replayWindow(tracking, reports, at('2026-09-12T12:00:00Z'), picture('1970-01-01T00:00:00Z'));
    expect(ancient?.from).toBe(at('2026-09-12T08:00:00Z'));
    expect(ancient?.to).toBe(at('2026-09-12T12:00:00Z'));

    // The positive twin, and the reason the bound is a day rather than nothing: a clock out by the
    // wrong hour, or by a time zone, or rolled over midnight, is a clock that drifted — and its
    // picture still has to be reachable on the rail.
    expect(replayWindow(tracking, reports, at('2026-09-12T12:00:00Z'), picture('2026-09-11T23:00:00Z'))?.from)
      .toBe(at('2026-09-11T23:00:00Z'));
    expect(replayWindow(tracking, reports, at('2026-09-12T12:00:00Z'), picture('2026-09-13T11:00:00Z'))?.to)
      .toBe(at('2026-09-13T11:00:00Z'));
  });
});
