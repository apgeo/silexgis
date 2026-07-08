// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { parsePropertiesSchema } from './propertiesSchema.ts';

describe('parsePropertiesSchema', () => {
  it('returns empty for null, invalid JSON and non-object schemas', () => {
    expect(parsePropertiesSchema(null)).toEqual([]);
    expect(parsePropertiesSchema(undefined)).toEqual([]);
    expect(parsePropertiesSchema('')).toEqual([]);
    expect(parsePropertiesSchema('not json')).toEqual([]);
    expect(parsePropertiesSchema('[1,2]')).toEqual([]);
    expect(parsePropertiesSchema('{"type":"string"}')).toEqual([]);
    expect(parsePropertiesSchema('{"type":"object"}')).toEqual([]);
  });

  it('parses the supported scalar kinds with titles, bounds and required flags', () => {
    const fields = parsePropertiesSchema(JSON.stringify({
      type: 'object',
      required: ['depth_m'],
      properties: {
        depth_m: { type: 'number', title: 'Depth (m)', minimum: 0, maximum: 500 },
        station_count: { type: 'integer' },
        notes: { type: 'string', title: 'Notes' },
        active: { type: 'boolean', title: 'Active' },
      },
    }));

    expect(fields).toEqual([
      { key: 'depth_m', label: 'Depth (m)', kind: 'number', required: true, enumValues: undefined, min: 0, max: 500 },
      { key: 'station_count', label: 'station_count', kind: 'integer', required: false, enumValues: undefined, min: undefined, max: undefined },
      { key: 'notes', label: 'Notes', kind: 'string', required: false, enumValues: undefined, min: undefined, max: undefined },
      { key: 'active', label: 'Active', kind: 'boolean', required: false, enumValues: undefined, min: undefined, max: undefined },
    ]);
  });

  it('maps string enums to select fields and skips unsupported property shapes', () => {
    const fields = parsePropertiesSchema(JSON.stringify({
      type: 'object',
      properties: {
        flow_kind: { type: 'string', title: 'Flow kind', enum: ['spring', 'sink'] },
        nested: { type: 'object', properties: {} },
        list: { type: 'array', items: { type: 'string' } },
        weird: { type: 42 },
      },
    }));

    expect(fields).toHaveLength(1);
    expect(fields[0]).toMatchObject({ key: 'flow_kind', kind: 'enum', enumValues: ['spring', 'sink'] });
  });
});
