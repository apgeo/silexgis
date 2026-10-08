// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { TrackingEvent } from '../api/hooks.ts';
import {
  CAVE_NOTE_MARK_COLOR,
  aboutPeople,
  caveNoteMarkWords,
  caveNoteMarkerId,
  caveNotesAt,
  wantedCaveNoteMarkers,
} from './caveNotes.ts';

const MODEL = 'model-1';
const OTHER_MODEL = 'model-2';

let sequence = 0;
function report(overrides: Partial<TrackingEvent> & { recordedAt: string }): TrackingEvent {
  return {
    id: `report-${sequence++}`,
    caverId: 'caver-ana',
    teamId: null,
    kind: 'atStation',
    surveyModelId: MODEL,
    stationName: null,
    toStationName: null,
    depthEnteredM: null,
    note: null,
    corrected: false,
    outsideDeclaredParts: false,
    ...overrides,
  } as TrackingEvent;
}

const caveNote = (id: string, recordedAt: string, overrides: Partial<TrackingEvent> = {}) =>
  report({ id, recordedAt, caverId: null, kind: 'caveNote', note: `words of ${id}`, ...overrides });

describe('the notes about the cave in force', () => {
  const log = [
    caveNote('late', '2026-09-12T11:00:00Z', { stationName: 'p.g.9' }),
    report({ recordedAt: '2026-09-12T10:30:00Z', stationName: 'p.g.4', note: 'All well' }),
    caveNote('early', '2026-09-12T09:00:00Z', { stationName: 'p.g.7' }),
    report({ recordedAt: '2026-09-12T08:10:00Z', kind: 'note', note: 'Radio check' }),
  ];

  it('are every one said so far on the live watch, newest first, and only the notes about the cave', () => {
    expect(caveNotesAt(log, null, MODEL).map((note) => note.id)).toEqual(['late', 'early']);
  });

  it('are those said at or before a replayed moment — a hazard does not lapse, and one not yet said is not there', () => {
    const at = (iso: string) => caveNotesAt(log, Date.parse(iso), MODEL).map((note) => note.id);

    expect(at('2026-09-12T08:59:59Z')).toEqual([]);
    expect(at('2026-09-12T09:00:00Z')).toEqual(['early']);
    expect(at('2026-09-12T10:59:00Z')).toEqual(['early']);
    expect(at('2026-09-12T12:00:00Z')).toEqual(['late', 'early']);
  });

  it('say whether the station they name is one of the drawing in use', () => {
    const notes = caveNotesAt(
      [
        caveNote('here', '2026-09-12T09:00:00Z', { stationName: 'p.g.7' }),
        caveNote('elsewhere', '2026-09-12T09:01:00Z', { stationName: 'x.2', surveyModelId: OTHER_MODEL }),
        // What a reader who may not be told the place is sent, and equally a note that names none.
        caveNote('unplaced', '2026-09-12T09:02:00Z', { stationName: null, surveyModelId: null }),
      ],
      null,
      MODEL,
    );

    expect(notes.map((note) => [note.id, note.stationName, note.onThisModel])).toEqual([
      ['unplaced', null, false],
      ['elsewhere', 'x.2', false],
      ['here', 'p.g.7', true],
    ]);
  });
});

describe('telling the reports about people from the notes about the cave', () => {
  it('leaves out exactly the rows about nobody', () => {
    const ana = report({ recordedAt: '2026-09-12T08:10:00Z', kind: 'entered' });
    const note = caveNote('rock', '2026-09-12T09:00:00Z', { stationName: 'p.g.7' });

    expect(aboutPeople([note, ana])).toEqual([ana]);
  });
});

describe('the marks of notes about the cave', () => {
  const notes = caveNotesAt(
    [
      caveNote('rock', '2026-09-12T09:00:00Z', { stationName: 'p.g.7', note: 'Loose rock' }),
      caveNote('water', '2026-09-12T09:01:00Z', { stationName: null, surveyModelId: null }),
      caveNote('other', '2026-09-12T09:02:00Z', { stationName: 'x.2', surveyModelId: OTHER_MODEL }),
    ],
    null,
    MODEL,
  );

  it('stand one per note that names a station of the drawing, under a name no person has', () => {
    const wanted = wantedCaveNoteMarkers(notes, (note) => `Cave: ${note.note}`);

    expect([...wanted]).toEqual([
      ['cave-note:rock', { station: 'p.g.7', label: 'Cave: Loose rock', color: CAVE_NOTE_MARK_COLOR }],
    ]);
    expect(caveNoteMarkerId('rock')).toBe('cave-note:rock');
  });

  it('carry the start of a long note on one line, and a short one whole', () => {
    expect(caveNoteMarkWords('Loose  rock\nabove the pitch')).toBe('Loose rock above the pitch');
    const cut = caveNoteMarkWords('x'.repeat(200));
    expect(cut).toHaveLength(48);
    expect(cut.endsWith('…')).toBe(true);
  });
});
