// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { HeldLine } from '../../api/hooks.ts';
import {
  candidateEntries,
  canonical,
  defaultFields,
  dependenciesOf,
  dependentsOf,
  fieldDiffs,
  fieldShape,
  fileOf,
  foreignDependenciesOf,
  groupByCave,
  placeLines,
  pointText,
  serverFieldDiffs,
  spanOf,
  titleOf,
} from './reviewModel.ts';

function line(id: string, overrides: Partial<HeldLine>): HeldLine {
  return {
    reviewId: id,
    section: 'rows',
    rowId: null,
    action: 'create',
    decision: 'pending',
    dependsOnReviewIds: [],
    acceptedFields: null,
    acceptableFields: [],
    note: null,
    decidedAt: null,
    decidedByUserId: null,
    failureCode: null,
    failureDetail: null,
    changedSince: false,
    proposal: null,
    ...overrides,
  } as HeldLine;
}

const cave = line('cave', {
  rowId: 'c1',
  proposal: { act: 'create', row: { id: 'c1', kind: 'cave', name: 'Invented Cave' } },
});
const entrance = line('ent', {
  rowId: 'e1',
  dependsOnReviewIds: ['cave'],
  proposal: { act: 'create', row: { id: 'e1', kind: 'caveEntrance', parentId: 'c1', name: 'Invented Entrance' } },
});
const recording = line('rec', {
  section: 'tripRecordings',
  rowId: 'r1',
  proposal: { act: 'create', recording: { id: 'r1', caveId: 'c1', title: 'A day out' } },
});
const point = line('pt', {
  section: 'tripPoints',
  dependsOnReviewIds: ['rec'],
  proposal: { act: 'create', point: { id: 'p1', recordingId: 'r1' } },
});
const person = line('who', {
  section: 'people',
  proposal: { person: { deviceUserId: 'd1', username: 'ion', firstName: 'Ion', lastName: 'A.' } },
});
const photo = line('doc', {
  section: 'documents',
  proposal: { act: 'create', row: { id: 'd9', title: 'Entrance photo' } },
});
const link = line('lnk', {
  section: 'documentLinks',
  dependsOnReviewIds: ['doc'],
  proposal: { act: 'create', row: { documentId: 'd9' }, targetId: 'c1' },
});

describe('where the lines of a batch are listed', () => {
  it('puts a cave, its places, its recordings, scans, and linked photos under one cave', () => {
    const placed = placeLines([cave, entrance, recording, point, link, photo, person]);
    for (const id of ['cave', 'ent', 'rec', 'pt', 'lnk', 'doc']) {
      expect(placed.get(id)?.caveId, id).toBe('c1');
    }
    expect(placed.get('who')?.caveId).toBeNull();
  });

  it('lists the caves first and what belongs to none last, each in the order a reviewer reads', () => {
    const groups = groupByCave([person, photo, link, point, recording, entrance, cave]);
    expect(groups.map((g) => g.caveId)).toEqual(['c1', null]);
    expect(groups[0].lines.map((l) => l.reviewId)).toEqual(['cave', 'ent', 'doc', 'lnk', 'rec', 'pt']);
  });

  it('survives lines that point at each other', () => {
    const a = line('a', { section: 'tripPoints', proposal: { point: { id: 'a', recordingId: 'b' } } });
    const b = line('b', { section: 'tripPoints', proposal: { point: { id: 'b', recordingId: 'a' } } });
    expect(placeLines([a, b]).get('a')?.caveId).toBeNull();
  });

  it('names a person by their name, and a candidate by its own name', () => {
    expect(titleOf(person)).toBe('Ion A.');
    expect(titleOf(line('imp', { section: 'candidates', proposal: { key: 'k', name: 'Candidate 1', decision: {} } }))).toBe(
      'Candidate 1',
    );
  });
});

describe('what a decision reaches', () => {
  it('follows waiting dependencies down and waiting dependents up', () => {
    const lines = [cave, entrance, recording, point];
    expect(dependenciesOf('pt', lines).map((l) => l.reviewId)).toEqual(['rec']);
    expect(dependenciesOf('ent', lines).map((l) => l.reviewId)).toEqual(['cave']);
    expect(dependentsOf('cave', lines).map((l) => l.reviewId)).toEqual(['ent']);
    expect(dependentsOf('rec', lines).map((l) => l.reviewId)).toEqual(['pt']);
  });

  it('does not count what is decided already, except a rejection that can be taken back', () => {
    const accepted = { ...cave, decision: 'accepted' } as HeldLine;
    const rejected = { ...cave, decision: 'rejected' } as HeldLine;
    expect(dependenciesOf('ent', [accepted, entrance])).toEqual([]);
    expect(dependenciesOf('ent', [rejected, entrance]).map((l) => l.reviewId)).toEqual(['cave']);
    expect(dependentsOf('cave', [cave, { ...entrance, decision: 'accepted' } as HeldLine])).toEqual([]);
  });
});

describe('the fields of an edit', () => {
  const edit = line('edit', {
    action: 'attach',
    rowId: 'f1',
    acceptableFields: ['name', 'description', 'properties', 'position'],
    proposal: {
      act: 'update',
      row: {
        id: 'f1',
        name: 'New name',
        description: 'Same text',
        properties: { b: 2, a: 1 },
        geometry: { type: 'Point', coordinates: [25.1, 45.2] },
      },
    },
  });
  const now = {
    name: 'Old name',
    description: 'Same text',
    properties: { a: 1, b: 2 },
    geometry: { type: 'Point', coordinates: [25.1, 45.2] },
  };

  it('marks only what differs, comparing documents whatever the order of their members', () => {
    const diffs = fieldDiffs(edit, now);
    expect(diffs.map((d) => [d.field, d.differs])).toEqual([
      ['name', true],
      ['description', false],
      ['properties', false],
      ['position', false],
    ]);
    expect(defaultFields(diffs)).toEqual(['name']);
  });

  it('shows every field as changed when the row now is unknown, and ticks them all', () => {
    const diffs = fieldDiffs(edit, undefined);
    expect(diffs.every((d) => d.differs)).toBe(true);
    expect(defaultFields(diffs)).toEqual(['name', 'description', 'properties', 'position']);
  });

  it('lists no position when the proposal carries none (held back from this reader)', () => {
    const hidden = line('h', {
      ...edit,
      proposal: { act: 'update', row: { id: 'f1', name: 'New name' } },
    });
    expect(fieldDiffs(hidden, now).map((d) => d.field)).toEqual(['name', 'description', 'properties']);
  });

  it('reads a point and compares equal documents as equal', () => {
    expect(pointText({ type: 'Point', coordinates: [25.1, 45.2] })).toBe('45.20000, 25.10000');
    expect(pointText({ type: 'Polygon', coordinates: [] })).toBeNull();
    expect(canonical({ b: 1, a: [1, { d: 1, c: 2 }] })).toBe(canonical({ a: [1, { c: 2, d: 1 }], b: 1 }));
  });
});

describe('dependencies outside the batch', () => {
  it('lists ids that are no line here, also through lines that are', () => {
    const a = line('a', { dependsOnReviewIds: ['gone'] });
    const b = line('b', { dependsOnReviewIds: ['a', 'also-gone'] });
    expect(foreignDependenciesOf('b', [a, b]).sort()).toEqual(['also-gone', 'gone']);
    expect(foreignDependenciesOf('a', [a, b])).toEqual(['gone']);
  });
});

describe('a file import candidate', () => {
  it('keeps the members that have a label and leaves ids and places out', () => {
    expect(
      candidateEntries({ action: 'Create', caveTypeCode: 'cave', targetFeatureId: 'x', longitude: 1, latitude: 2, createTrip: true }),
    ).toEqual([
      ['action', 'Create'],
      ['caveTypeCode', 'cave'],
      ['createTrip', 'true'],
    ]);
  });
});

describe('an edit the server compared', () => {
  it('lists only the fields the line may be accepted by, with what the server withheld marked', () => {
    const edit = line('rec', {
      section: 'tripRecordings',
      action: 'attach',
      acceptableFields: ['title', 'cave'],
      fields: [
        { field: 'title', current: 'Before', proposed: 'After', differs: true, currentWithheld: false },
        { field: 'cave', current: null, proposed: 'c2', differs: true, currentWithheld: true },
        { field: 'log', current: 'x', proposed: 'y', differs: true, currentWithheld: false },
      ],
    });
    const diffs = serverFieldDiffs(edit);
    expect(diffs.map((d) => d.field)).toEqual(['title', 'cave']);
    expect(diffs[1]).toMatchObject({ current: undefined, proposed: 'c2', withheld: true });
    expect(defaultFields(diffs)).toEqual(['title', 'cave']);
    expect(serverFieldDiffs(line('none', {}))).toEqual([]);
  });

  it('reads each field by its shape', () => {
    expect(fieldShape('time')).toBe('moment');
    expect(fieldShape('times')).toBe('span');
    expect(fieldShape('place')).toBe('feature');
    expect(fieldShape('caveArea')).toBe('feature');
    expect(fieldShape('file')).toBe('file');
    expect(fieldShape('mapType')).toBe('mapType');
    expect(fieldShape('notes')).toBe('text');
    expect(spanOf({ startedAt: '2026-09-12T08:00:00Z', endedAt: null })).toEqual({ start: '2026-09-12T08:00:00Z', end: null });
    expect(fileOf({ fileName: 'plan.jpg', sizeBytes: 10 })).toEqual({ name: 'plan.jpg', size: 10 });
    expect(fileOf(null)).toBeNull();
  });
});
