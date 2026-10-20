// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  conditionOf,
  describeComparison,
  formatCondition,
  formatSigned,
  negationShown,
  readComparison,
  resolveTyped,
  type Comparison,
  type TypedCondition,
} from './typedComparison.ts';

function read(text: string, kind: 'number' | 'day' = 'number', current: Parameters<typeof readComparison>[2] = 'equals') {
  const reading = readComparison(text, kind, current);
  return reading.kind === 'read' ? reading.comparison : reading.kind;
}

const n = (op: Comparison['op'], values: number[], negated = false, unit: string | null = null): Comparison =>
  ({ op, negated, values, unit });

describe('a typed number comparison', () => {
  it.each([
    ['>300', n('greaterThan', [300])],
    ['> 300', n('greaterThan', [300])],
    ['>=300', n('greaterOrEqual', [300])],
    ['=>300', n('greaterOrEqual', [300])],
    ['≥300', n('greaterOrEqual', [300])],
    ['<30', n('lessThan', [30])],
    ['<=30', n('lessOrEqual', [30])],
    ['=<30', n('lessOrEqual', [30])],
    ['≤30', n('lessOrEqual', [30])],
    ['=300', n('equals', [300])],
    ['==300', n('equals', [300])],
    ['!=300', n('equals', [300], true)],
    ['<>300', n('equals', [300], true)],
    ['≠300', n('equals', [300], true)],
  ])('reads %s', (text, expected) => {
    expect(read(text, 'number', 'greaterOrEqual')).toEqual(expected);
  });

  it('reads a bare number as the comparison already chosen, and as equal to under any other', () => {
    expect(read('300', 'number', 'equals')).toEqual(n('equals', [300]));
    expect(read('300', 'number', 'greaterOrEqual')).toEqual(n('greaterOrEqual', [300]));
    expect(read('300', 'number', 'lessThan')).toEqual(n('lessThan', [300]));
    expect(read('300', 'number', 'between')).toEqual(n('equals', [300]));
    expect(read('300', 'number', 'isEmpty')).toEqual(n('equals', [300]));
  });

  it.each([
    ['300..500', [300, 500]],
    ['300 .. 500', [300, 500]],
    ['300-500', [300, 500]],
    ['300 - 500', [300, 500]],
    ['300–500', [300, 500]],
    ['300—500', [300, 500]],
    ['-10..-5', [-10, -5]],
    ['-10--5', [-10, -5]],
    ['-10 - -5', [-10, -5]],
    ['-10-5', [-10, 5]],
    ['1.5..2.5', [1.5, 2.5]],
    ['2,5-3,5', [2.5, 3.5]],
  ])('reads the range %s, both ends included', (text, values) => {
    expect(read(text)).toEqual(n('between', values));
  });

  it('puts a range written backwards the right way round', () => {
    expect(read('500..300')).toEqual(n('between', [300, 500]));
  });

  it('reads a leading minus as a negative number, never as an open range', () => {
    expect(read('-5')).toEqual(n('equals', [-5]));
    expect(read('>-5')).toEqual(n('greaterThan', [-5]));
    expect(read('<-5')).toEqual(n('lessThan', [-5]));
  });

  it('takes a comma as the decimal mark and never as a thousands separator', () => {
    expect(read('2,5')).toEqual(n('equals', [2.5]));
    expect(read('>=2,5')).toEqual(n('greaterOrEqual', [2.5]));
    expect(read('1,000')).toEqual(n('equals', [1]));
    expect(read('.5')).toEqual(n('equals', [0.5]));
    expect(read('1.000,5')).toBe('unreadable');
    expect(read('1 000')).toBe('unreadable');
  });

  it('tolerates the unit m and keeps it to show back', () => {
    expect(read('300 m')).toEqual(n('equals', [300], false, 'm'));
    expect(read('300m')).toEqual(n('equals', [300], false, 'm'));
    expect(read('>300 M')).toEqual(n('greaterThan', [300], false, 'm'));
    expect(read('300m..500m')).toEqual(n('between', [300, 500], false, 'm'));
    expect(read('300..500 m')).toEqual(n('between', [300, 500], false, 'm'));
  });

  it.each([
    'abc', '300 km', '>', '>>300', '>300..500', '!=300..500', '300..', '..500', '1-2-3', '300-', '- 5', '3e5', '0x10',
  ])('refuses %s rather than guessing', (text) => {
    expect(read(text)).toBe('unreadable');
  });

  it('says nothing for an empty box', () => {
    expect(read('')).toBe('empty');
    expect(read('   ')).toBe('empty');
  });
});

describe('a typed day comparison', () => {
  const d = (op: Comparison['op'], values: string[], negated = false): Comparison => ({ op, negated, values, unit: null });

  it('reads both ways a day is written, and refuses a day the calendar has not got', () => {
    expect(read('2024-05-01', 'day', 'greaterThan')).toEqual(d('greaterThan', ['2024-05-01']));
    expect(read('1.5.2024', 'day', 'greaterThan')).toEqual(d('greaterThan', ['2024-05-01']));
    expect(read('2024-5-1', 'day', 'greaterThan')).toEqual(d('greaterThan', ['2024-05-01']));
    expect(read('2024-02-30', 'day', 'greaterThan')).toBe('unreadable');
    expect(read('31.4.2024', 'day', 'greaterThan')).toBe('unreadable');
  });

  it.each([
    ['>2024-05-01', 'greaterThan', false],
    ['>=2024-05-01', 'greaterOrEqual', false],
    ['<2024-05-01', 'lessThan', false],
    ['≤2024-05-01', 'lessOrEqual', false],
    ['=2024-05-01', 'equals', false],
    ['!=2024-05-01', 'equals', true],
  ] as const)('reads %s', (text, op, negated) => {
    expect(read(text, 'day', 'lessThan')).toEqual(d(op, ['2024-05-01'], negated));
  });

  it('reads a range only with two dots, since a day is written with dashes', () => {
    expect(read('2024-05-01..2024-06-30', 'day')).toEqual(d('between', ['2024-05-01', '2024-06-30']));
    expect(read('1.5.2024..30.6.2024', 'day')).toEqual(d('between', ['2024-05-01', '2024-06-30']));
    expect(read('2024-06-30..2024-05-01', 'day')).toEqual(d('between', ['2024-05-01', '2024-06-30']));
    expect(read('2024-05-01-2024-06-30', 'day')).toBe('unreadable');
    expect(read('2024-05-01–2024-06-30', 'day')).toBe('unreadable');
  });

  it('reads a bare day under a range as that one day', () => {
    expect(read('2024-05-01', 'day', 'between')).toEqual(d('equals', ['2024-05-01']));
  });

  it('refuses a number where a day belongs', () => {
    expect(read('300', 'day')).toBe('unreadable');
    expect(read('>2024', 'day')).toBe('unreadable');
  });
});

describe('the condition a typed comparison makes', () => {
  it('keeps a number comparison as written', () => {
    expect(conditionOf(n('between', [300, 500]), 'number')).toEqual({
      op: 'between', negated: false, values: [{ type: 'number', value: 300 }, { type: 'number', value: 500 }],
    });
    expect(conditionOf(n('equals', [300], true), 'number')).toEqual({
      op: 'equals', negated: true, values: [{ type: 'number', value: 300 }],
    });
  });

  it('makes a day “equal to” the whole of that day, and its negation not that day', () => {
    const on = conditionOf({ op: 'equals', negated: true, values: ['2024-05-01'], unit: null }, 'day');
    expect(on.op).toBe('between');
    expect(on.negated).toBe(true);
    expect((on.values[0] as { value: string }).value.startsWith('2024-05-01T00:00:00.000')).toBe(true);
    expect((on.values[1] as { value: string }).value.startsWith('2024-05-01T23:59:59.999999')).toBe(true);
  });

  it('bounds the other day comparisons as a picked day is bounded', () => {
    const after = conditionOf({ op: 'greaterThan', negated: false, values: ['2024-05-01'], unit: null }, 'day');
    expect((after.values[0] as { value: string }).value.slice(0, 26)).toBe('2024-05-01T23:59:59.999999');
    const from = conditionOf({ op: 'greaterOrEqual', negated: false, values: ['2024-05-01'], unit: null }, 'day');
    expect((from.values[0] as { value: string }).value.slice(0, 23)).toBe('2024-05-01T00:00:00.000');
  });
});

describe('a row’s condition written back into its box', () => {
  const row = (op: TypedCondition['op'], values: number[], negated = false): TypedCondition =>
    ({ op, negated, values: values.map((value) => ({ type: 'number', value })) });

  it('shows a single value bare, a range with two dots, and only a negated equals with a sign', () => {
    expect(formatCondition(row('greaterOrEqual', [300]), 'number')).toBe('300');
    expect(formatCondition(row('between', [300, 500]), 'number')).toBe('300..500');
    expect(formatCondition(row('equals', [300], true), 'number')).toBe('!=300');
    expect(formatCondition(row('greaterThan', [300], true), 'number')).toBe('300');
    expect(formatCondition(row('greaterThan', []), 'number')).toBeNull();
  });

  it('reads back into the same condition with the same comparison chosen', () => {
    for (const [text, current] of [['>300', 'equals'], ['!=300', 'equals'], ['300..500', 'equals'], ['<=2,5', 'equals']] as const) {
      const first = read(text, 'number', current) as Comparison;
      const condition = conditionOf(first, 'number');
      const again = read(formatCondition(condition, 'number')!, 'number', condition.op) as Comparison;
      expect(conditionOf(again, 'number')).toEqual(condition);
    }
    for (const text of ['>2024-05-01', '!=2024-05-01', '2024-05-01..2024-06-30', '=2024-05-01']) {
      const condition = conditionOf(read(text, 'day', 'lessThan') as Comparison, 'day');
      const again = read(formatCondition(condition, 'day')!, 'day', condition.op) as Comparison;
      expect(conditionOf(again, 'day')).toEqual(condition);
    }
  });

  it('says when the negation is spelled in the box', () => {
    expect(negationShown(row('equals', [300], true), 'number')).toBe(true);
    expect(negationShown(row('greaterThan', [300], true), 'number')).toBe(false);
    expect(negationShown(row('equals', [300]), 'number')).toBe(false);
  });
});

describe('a comparison written out whole for a box with no comparison list', () => {
  it('writes the box’s own comparison bare and every other with its sign, and reads back the same', () => {
    const cases: [Comparison, string][] = [
      [n('greaterOrEqual', [300]), '300'],
      [n('greaterThan', [300]), '>300'],
      [n('lessThan', [30]), '<30'],
      [n('lessOrEqual', [30]), '<=30'],
      [n('equals', [2.5]), '=2.5'],
      [n('equals', [300], true), '!=300'],
      [n('between', [-10, -5]), '-10..-5'],
    ];
    for (const [comparison, text] of cases) {
      expect(formatSigned(comparison, 'greaterOrEqual')).toBe(text);
      expect(read(text, 'number', 'greaterOrEqual')).toEqual(comparison);
    }
  });
});

describe('typing into a row', () => {
  const row = (op: TypedCondition['op'], value: number, negated: boolean): TypedCondition =>
    ({ op, negated, values: [{ type: 'number', value }] });

  it('lets “equal to” and “not equal to” decide the negation', () => {
    expect(resolveTyped(row('greaterThan', 1, false), n('equals', [3], true), 'number').negated).toBe(true);
    expect(resolveTyped(row('equals', 1, true), n('equals', [3]), 'number').negated).toBe(false);
  });

  it('keeps a negation ticked by hand, and drops one only the box was spelling', () => {
    expect(resolveTyped(row('greaterThan', 1, true), n('lessThan', [3]), 'number'))
      .toEqual({ op: 'lessThan', negated: true, values: [{ type: 'number', value: 3 }] });
    expect(resolveTyped(row('equals', 1, true), n('greaterThan', [3]), 'number'))
      .toEqual({ op: 'greaterThan', negated: false, values: [{ type: 'number', value: 3 }] });
  });
});

describe('what a comparison is said to ask', () => {
  it('names the comparison and carries the unit as written', () => {
    expect(describeComparison(n('greaterOrEqual', [300], false, 'm'), 'number'))
      .toEqual({ key: 'filters.typed.number.greaterOrEqual', values: { value: '300 m' } });
    expect(describeComparison(n('equals', [300], true), 'number'))
      .toEqual({ key: 'filters.typed.number.notEquals', values: { value: '300' } });
    expect(describeComparison(n('between', [2.5, 3]), 'number'))
      .toEqual({ key: 'filters.typed.number.between', values: { low: '2.5', high: '3' } });
    expect(describeComparison({ op: 'equals', negated: false, values: ['2024-05-01'], unit: null }, 'day'))
      .toEqual({ key: 'filters.typed.day.equals', values: { value: '2024-05-01' } });
  });
});
