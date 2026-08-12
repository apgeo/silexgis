// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  applyFeatureRestore,
  applyRestore,
  applyTripRestore,
  changeRows,
  formatValue,
  restorableProps,
  toWriteField,
  toWriteValue,
} from './historyModel.ts';

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

  it('applyFeatureRestore maps Geom to the geometry field and parses a Properties string', () => {
    const current = {
      name: 'F',
      geometry: { type: 'Point', coordinates: [1, 1] },
      properties: { depth_m: 20 },
    };
    const changes = {
      Geom: { old: 'POINT (25 45)', new: 'POINT (26 46)' },
      Properties: { old: '{"depth_m":12.5}', new: '{"depth_m":20}' },
    };

    const restored = applyFeatureRestore(current, changes, ['Geom', 'Properties']);

    expect(restored.geometry).toEqual({ type: 'Point', coordinates: [25, 45] });
    expect(restored.properties).toEqual({ depth_m: 12.5 }); // parsed from the audit JSON string
    expect((restored as Record<string, unknown>).geom).toBeUndefined(); // no stray generic key
  });
});
