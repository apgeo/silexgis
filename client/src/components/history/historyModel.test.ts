// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  applyRestore,
  applyTripRestore,
  changeRows,
  entityLabelKey,
  formatValue,
  groupByChangeSet,
  isGeometrySummary,
  restorableProps,
  restoreRequest,
  rosterTimesAct,
  serverRestorableProps,
  toWriteField,
  toWriteValue,
} from './historyModel.ts';

describe('the entry for roster times taken from the tracking log', () => {
  it('is read as the act it records', () => {
    expect(
      rosterTimesAct({
        RosterTimes: { new: 'fromTracking' },
        TimeZone: { new: 'Europe/Bucharest' },
        People: { new: 2 },
        Rows: { new: 3 },
      }),
    ).toEqual({ timeZone: 'Europe/Bucharest', people: 2, rows: 3 });
  });

  it('leaves out a fact the entry does not state, rather than inventing one', () => {
    expect(rosterTimesAct({ RosterTimes: { new: 'fromTracking' }, People: { new: 'two' } })).toEqual({
      timeZone: null,
      people: null,
      rows: null,
    });
  });

  it('is not seen in an ordinary change, nor in nothing', () => {
    expect(rosterTimesAct({ Description: { old: 'a', new: 'b' } })).toBeNull();
    expect(rosterTimesAct({ RosterTimes: { old: 'x', new: 'somethingElse' } })).toBeNull();
    expect(rosterTimesAct(null)).toBeNull();
  });
});

describe('historyModel', () => {
  it('builds change rows and appends a redacted row for stripped properties', () => {
    const changes = {
      Description: { old: 'a', new: 'b' },
    };
    const rows = changeRows(changes, ['Geom']);

    expect(rows).toContainEqual({ prop: 'Description', old: 'a', new: 'b', redacted: false });
    expect(rows).toContainEqual({ prop: 'Geom', old: undefined, new: undefined, redacted: true });
  });

  it('formats scalar and object values, and null as an em dash', () => {
    expect(formatValue(null)).toBe('—');
    expect(formatValue(undefined)).toBe('—');
    expect(formatValue('hi')).toBe('hi');
    expect(formatValue(42)).toBe('42');
    expect(formatValue(true)).toBe('true');
    expect(formatValue({ a: 1 })).toBe('{"a":1}');
  });

  it('maps PascalCase audit props to camelCase write fields', () => {
    expect(toWriteField('ClosestAddress')).toBe('closestAddress');
    expect(toWriteField('Geom')).toBe('geom');
    expect(toWriteField('')).toBe('');
  });

  it('converts geometry WKT to a GeoJSON geometry, leaving non-geometry strings alone', () => {
    expect(toWriteValue('POINT (25 45)')).toEqual({ type: 'Point', coordinates: [25, 45] });
    expect(toWriteValue('Str. Secreta 5')).toBe('Str. Secreta 5');
    expect(toWriteValue(900)).toBe(900);
    expect(toWriteValue('POINT (bad')).toBe('POINT (bad'); // malformed WKT passes through
  });

  it('restorableProps drops redacted rows and rows with no old value', () => {
    const rows = changeRows({ Name: { old: 'x', new: 'y' }, Created: { old: null, new: 'z' } }, ['Geom']);
    expect(restorableProps(rows)).toEqual(['Name', 'Created']);
  });

  it('recognises a summarised geometry and never offers it for restore', () => {
    const summary = { geometryType: 'MultiLineString', points: 48211, sha256: 'ab12', omitted: true };
    expect(isGeometrySummary(summary)).toBe(true);
    expect(isGeometrySummary({ ...summary, omitted: false })).toBe(false);
    expect(isGeometrySummary('POINT (1 2)')).toBe(false);
    expect(isGeometrySummary(null)).toBe(false);

    const rows = changeRows({ Geom: { old: summary, new: { ...summary, sha256: 'cd34' } }, Name: { old: 'a', new: 'b' } }, []);
    expect(restorableProps(rows)).toEqual(['Name']);
  });

  it('applyRestore overlays old values (mapped + WKT→GeoJSON) and keeps other fields', () => {
    const current = {
      name: 'New name',
      description: 'New desc',
      geom: { type: 'Point', coordinates: [1, 1] },
    };
    const changes = {
      Name: { old: 'Old name', new: 'New name' },
      Description: { old: 'Old desc', new: 'New desc' },
      Geom: { old: 'POINT (25 45)', new: 'POINT (26 46)' },
    };

    const restored = applyRestore(current, changes, ['Name', 'Geom']);

    expect(restored.name).toBe('Old name');
    expect(restored.geom).toEqual({ type: 'Point', coordinates: [25, 45] });
    expect(restored.description).toBe('New desc'); // untouched — keeps the current value
  });

  it('applyTripRestore mentions no report section the restore did not name', () => {
    const current = {
      title: 'New title',
      fieldData: { conditions: 'dry' },
      logistics: { permit_reference: 'P-1' },
      safety: { incident_summary: 'slip' },
    };
    const changes = { Title: { old: 'Old title', new: 'New title' } };

    const restored = applyTripRestore(current, changes, ['Title']);

    expect(restored.title).toBe('Old title');
    // Not three emptied sections — no section at all, which is what tells the server to leave
    // them alone rather than measure them against the purpose's schema as it stands now.
    expect(restored.fieldData).toBeNull();
    expect(restored.logistics).toBeNull();
    expect(restored.safety).toBeNull();
  });

  it('applyTripRestore says nothing about the trip’s caves', () => {
    // The list on a loaded trip is short of every cave that reader may not be told about, and it
    // is not restorable in the first place. Echoing it back would be read as an instruction to
    // drop the caves it does not mention — given by somebody who was never shown them.
    const current = {
      title: 'New title',
      caveIds: ['5b4c3d2e-1111-4222-8333-444455556666'],
      fieldData: null,
      logistics: null,
      safety: null,
    };
    const changes = { Title: { old: 'Old title', new: 'New title' } };

    const restored = applyTripRestore(current, changes, ['Title']);

    expect(restored.title).toBe('Old title');
    expect(restored.caveIds).toBeNull();
  });

  it('applyTripRestore restores a named section, parsed from the audit JSON string', () => {
    const current = { title: 'T', fieldData: { conditions: 'wet' }, logistics: {}, safety: {} };
    const changes = {
      FieldData: { old: '{"conditions":"dry"}', new: '{"conditions":"wet"}' },
    };

    const restored = applyTripRestore(current, changes, ['FieldData']);

    expect(restored.fieldData).toEqual({ conditions: 'dry' });
    expect(restored.logistics).toBeNull();
    expect(restored.safety).toBeNull();
  });

  it('serverRestorableProps keeps a summarised geometry the server may hold in full, and drops hidden rows', () => {
    const summary = { omitted: true, geometryType: 'LineString', points: 9000, sha256: 'abc' };
    const rows = changeRows(
      { Name: { old: 'A', new: 'B' }, Geom: { old: summary, new: summary }, Added: { new: 'x' } },
      ['Altitude'],
    );

    expect(serverRestorableProps(rows)).toEqual(['Name', 'Geom']);
    // The client-composed path still cannot write a summary back.
    expect(restorableProps(rows)).toEqual(['Name']);
  });

  it('restoreRequest names the event, its own entity and the fields wanted', () => {
    expect(restoreRequest({ id: 42, entityId: 'entrance-1' }, ['Name', 'Geom'])).toEqual({
      entityType: 'feature',
      entityId: 'entrance-1',
      auditEntryId: 42,
      fields: ['Name', 'Geom'],
    });
  });

  it('groupByChangeSet puts one change set under one entry, where its newest event sits', () => {
    const events = [
      { id: 5, changeSetId: 'a' },
      { id: 4, changeSetId: null },
      { id: 3, changeSetId: 'a' },
      { id: 2, changeSetId: 'b' },
      { id: 1, changeSetId: 'a' },
    ];

    const groups = groupByChangeSet(events);

    expect(groups.map((g) => g.key)).toEqual(['set-a', 'event-4', 'event-2']);
    expect(groups[0].events.map((e) => e.id)).toEqual([5, 3, 1]);
    // A set with one event in this timeline, and an event from before sets, stand alone.
    expect(groups[1].changeSetId).toBeNull();
    expect(groups[2].changeSetId).toBeNull();
  });

  it('entityLabelKey drops the feature qualifier a colon would make a namespace of', () => {
    expect(entityLabelKey('Feature:CaveEntrance')).toBe('CaveEntrance');
    expect(entityLabelKey('Feature:Generic')).toBe('SurfaceFeature');
    expect(entityLabelKey('TripLog')).toBe('TripLog');
  });
});
