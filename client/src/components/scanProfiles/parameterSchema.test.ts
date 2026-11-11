// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  changedFields,
  fieldDiffers,
  fieldValue,
  listText,
  parameterGroups,
  parseList,
  sameValue,
  valueAt,
  withValueAt,
} from './parameterSchema.ts';

/**
 * The two shapes a step's schema comes in: flat (the tiler, the thinning) and HDMapping's
 * `{values: {section: {key}}}`. The keys and numbers below are invented in the shape the server
 * writes, not copied from any tool's dump.
 */

const flat = {
  type: 'object',
  properties: {
    method: { type: 'string', enum: ['voxel', 'everyNth'], default: 'voxel', description: 'How to thin.' },
    voxelCm: { type: 'number', exclusiveMinimum: 0, maximum: 1000, default: 2 },
    everyNth: { type: 'integer', minimum: 1, maximum: 10000, default: 2 },
    rgb: { type: 'boolean', default: true },
    voxelsCm: { type: 'array', items: { type: 'number' }, default: [1, 2, 5] },
  },
  additionalProperties: false,
};

const sectioned = {
  type: 'object',
  properties: {
    values: {
      type: 'object',
      properties: {
        alpha_section: {
          type: 'object',
          properties: {
            use_thing: { type: 'boolean', default: false },
            step_size: { type: 'number', default: 0.5 },
            tool_name: { type: 'string', const: 'fixed-text', default: 'fixed-text' },
          },
        },
        beta_section: {
          type: 'object',
          properties: { rounds: { type: 'integer', minimum: -2147483648, maximum: 2147483647, default: 30 } },
        },
      },
    },
  },
};

const sectionedDefaults = {
  values: { alpha_section: { use_thing: false, step_size: 0.5, tool_name: 'fixed-text' }, beta_section: { rounds: 30 } },
};

describe('reading a schema into groups', () => {
  it('makes one top-level group for a flat step, each setting by its kind', () => {
    const groups = parameterGroups(flat, {});
    expect(groups).toHaveLength(1);
    expect(groups[0]!.title).toBe('');
    expect(groups[0]!.fields.map((f) => [f.key, f.kind])).toEqual([
      ['method', 'choice'],
      ['voxelCm', 'number'],
      ['everyNth', 'integer'],
      ['rgb', 'boolean'],
      ['voxelsCm', 'numberList'],
    ]);
    const method = groups[0]!.fields[0]!;
    expect(method.options).toEqual(['voxel', 'everyNth']);
    expect(method.defaultValue).toBe('voxel');
    expect(method.description).toBe('How to thin.');
    expect(groups[0]!.fields[1]!.exclusiveMinimum).toBe(0);
  });

  it('groups a sectioned step by section, with no group for the wrapper, and takes defaults from the answer', () => {
    const groups = parameterGroups(sectioned, {
      values: { alpha_section: { step_size: 0.25 }, beta_section: { rounds: 12 } },
    });
    expect(groups.map((g) => [g.id, g.title])).toEqual([
      ['values.alpha_section', 'alpha_section'],
      ['values.beta_section', 'beta_section'],
    ]);
    const alpha = groups[0]!.fields;
    expect(alpha.map((f) => f.kind)).toEqual(['boolean', 'number', 'fixed']);
    // The defaults the server answered beside the schema win over the schema's own.
    expect(alpha[1]!.defaultValue).toBe(0.25);
    expect(groups[1]!.fields[0]!.defaultValue).toBe(12);
    // A setting missing from the answered defaults keeps the schema's.
    expect(alpha[0]!.defaultValue).toBe(false);
    expect(alpha[2]!.path).toEqual(['values', 'alpha_section', 'tool_name']);
  });

  it('reads nothing out of something that is not a schema', () => {
    expect(parameterGroups(null, null)).toEqual([]);
    expect(parameterGroups({ type: 'object', properties: {} }, {})).toEqual([]);
  });
});

describe('values against defaults', () => {
  const groups = parameterGroups(sectioned, sectionedDefaults);
  const [useThing, stepSize] = groups[0]!.fields;
  const rounds = groups[1]!.fields[0]!;

  it('shows the default for a setting a partial profile leaves out, and does not count it changed', () => {
    const values = { values: { beta_section: { rounds: 40 } } };
    expect(fieldValue(stepSize!, values)).toBe(0.5);
    expect(fieldDiffers(stepSize!, values)).toBe(false);
    expect(fieldDiffers(rounds, values)).toBe(true);
    expect(changedFields(groups, values).map((f) => f.key)).toEqual(['rounds']);
  });

  it('does not count a setting written out at its default as changed', () => {
    expect(changedFields(groups, sectionedDefaults)).toEqual([]);
    expect(fieldDiffers(useThing!, { values: { alpha_section: { use_thing: true } } })).toBe(true);
  });

  it('writes one setting back without touching its neighbours or the original', () => {
    const before = { values: { alpha_section: { use_thing: false, step_size: 0.5 } } };
    const after = withValueAt(before, ['values', 'alpha_section', 'step_size'], 0.75);
    expect(after).toEqual({ values: { alpha_section: { use_thing: false, step_size: 0.75 } } });
    expect(before.values.alpha_section.step_size).toBe(0.5);
    // A section the profile did not name is made.
    expect(withValueAt(before, ['values', 'beta_section', 'rounds'], 3)).toEqual({
      values: { alpha_section: { use_thing: false, step_size: 0.5 }, beta_section: { rounds: 3 } },
    });
    expect(valueAt(after, ['values', 'missing', 'x'])).toBeUndefined();
  });

  it('compares lists and objects by what they hold', () => {
    expect(sameValue([1, 2], [1, 2])).toBe(true);
    expect(sameValue([1, 2], [2, 1])).toBe(false);
    expect(sameValue({ a: 1 }, { a: 1 })).toBe(true);
    expect(sameValue({ a: 1 }, { a: 1, b: 2 })).toBe(false);
  });
});

describe('list settings as typed', () => {
  it('round-trips a number list and refuses one holding a word', () => {
    expect(listText([1, 2.5, 10])).toBe('1, 2.5, 10');
    expect(parseList('1, 2.5 ,10,', 'numberList')).toEqual([1, 2.5, 10]);
    expect(parseList('1, two', 'numberList')).toBeNull();
    expect(parseList('a, b', 'textList')).toEqual(['a', 'b']);
  });
});
