// SPDX-License-Identifier: AGPL-3.0-or-later
import dayjs from 'dayjs';
import { dayBound, pickedDay } from './dayBounds.ts';
import type { FilterOp, FilterValue } from './types.ts';

/**
 * A comparison typed with signs — `>300`, `<=30`, `!=300`, `300..500` — read into the condition it
 * means.
 *
 * Writing a sign is quicker than choosing a comparison from a list, and it is how most people
 * already write one. The reading is deliberately narrow: a form it does not know is unreadable
 * rather than guessed at, because a guess nobody sees is a condition nobody asked.
 *
 * What is read:
 * - `>v` more than, `>=v` `=>v` `≥v` at least, `<v` less than, `<=v` `=<v` `≤v` at most;
 * - `=v` `==v` equal to, `!=v` `<>v` `≠v` not equal to (a negated equals, so it also takes in
 *   whatever has no value recorded);
 * - `a..b` between, both ends included; a number range may also be written `a-b` or `a–b`. A day
 *   range only with `..`, because a day is itself written with dashes;
 * - a bare `v` means the comparison already chosen, if that is a single-value one, and equal to
 *   otherwise.
 *
 * Numbers take a comma or a point as the decimal mark (`2,5` is two and a half — a comma is never
 * read as a thousands separator) and may carry the unit `m`, which is shown back and changes
 * nothing. Days are written `2024-05-01` or `1.5.2024`. A day compared "equal to" means the whole
 * of that day.
 */

export type TypedKind = 'number' | 'day';

/** The comparisons a typed value can ask for. "Not equal to" is `equals` with `negated`. */
export type ComparisonOp = 'equals' | 'greaterThan' | 'greaterOrEqual' | 'lessThan' | 'lessOrEqual' | 'between';

/** What was read: numbers for a number, `YYYY-MM-DD` days for a day. */
export interface Comparison {
  op: ComparisonOp;
  negated: boolean;
  values: (number | string)[];
  /** The unit as written after the value, or null. */
  unit: string | null;
}

export type Reading =
  | { kind: 'empty' }
  | { kind: 'read'; comparison: Comparison }
  | { kind: 'unreadable' };

/** A condition row's own parts, as the editor holds them. */
export interface TypedCondition {
  op: FilterOp;
  values: FilterValue[];
  negated: boolean;
}

const SINGLE_OPS: ComparisonOp[] = ['equals', 'greaterThan', 'greaterOrEqual', 'lessThan', 'lessOrEqual'];

// Longest first, so `>=` is not read as `>` followed by `=300`.
const SIGNS: [string, ComparisonOp, boolean][] = [
  ['>=', 'greaterOrEqual', false],
  ['=>', 'greaterOrEqual', false],
  ['≥', 'greaterOrEqual', false],
  ['<=', 'lessOrEqual', false],
  ['=<', 'lessOrEqual', false],
  ['≤', 'lessOrEqual', false],
  ['!=', 'equals', true],
  ['<>', 'equals', true],
  ['≠', 'equals', true],
  ['==', 'equals', false],
  ['>', 'greaterThan', false],
  ['<', 'lessThan', false],
  ['=', 'equals', false],
];

const NUMBER = String.raw`-?(?:\d+(?:[.,]\d+)?|[.,]\d+)`;
const UNIT = String.raw`(?:\s*(m))?`;
const ONE_NUMBER = new RegExp(String.raw`^(${NUMBER})${UNIT}$`, 'i');
const NUMBER_RANGE = new RegExp(String.raw`^(${NUMBER})${UNIT}\s*(?:\.\.|[-–—])\s*(${NUMBER})${UNIT}$`, 'i');
const DAY = String.raw`\d{4}-\d{1,2}-\d{1,2}|\d{1,2}\.\d{1,2}\.\d{4}`;
const ONE_DAY = new RegExp(String.raw`^(${DAY})$`);
const DAY_RANGE = new RegExp(String.raw`^(${DAY})\s*\.\.\s*(${DAY})$`);

function numberOf(written: string): number | null {
  const value = Number(written.replace(',', '.'));
  return Number.isFinite(value) ? value : null;
}

/** A written day as `YYYY-MM-DD`, or null when it names no day of the calendar (the 30th of February). */
function dayOf(written: string): string | null {
  const iso = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(written);
  const dotted = /^(\d{1,2})\.(\d{1,2})\.(\d{4})$/.exec(written);
  const [year, month, day] = iso
    ? [Number(iso[1]), Number(iso[2]), Number(iso[3])]
    : dotted
      ? [Number(dotted[3]), Number(dotted[2]), Number(dotted[1])]
      : [NaN, NaN, NaN];
  const date = new Date(year, month - 1, day);
  if (Number.isNaN(date.getTime()) || date.getFullYear() !== year || date.getMonth() !== month - 1 || date.getDate() !== day) {
    return null;
  }
  return `${String(year).padStart(4, '0')}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

/** The comparison a bare value means: the one already chosen when it takes one value, else "equal to". */
function bareOp(current: FilterOp): ComparisonOp {
  // A day row "equal to" a day is stored as a range over that day, so a bare day typed into it
  // lands here too and means that day again.
  return SINGLE_OPS.find((o) => o === current) ?? 'equals';
}

/**
 * What a typed text asks.
 *
 * `current` is the comparison the row already has; it gives a bare value its meaning, and nothing
 * else.
 */
export function readComparison(text: string, kind: TypedKind, current: FilterOp): Reading {
  const trimmed = text.trim();
  if (trimmed === '') {
    return { kind: 'empty' };
  }

  const sign = SIGNS.find(([written]) => trimmed.startsWith(written));
  const rest = sign === undefined ? trimmed : trimmed.slice(sign[0].length).trim();

  if (kind === 'number') {
    const one = ONE_NUMBER.exec(rest);
    if (one) {
      const value = numberOf(one[1]!);
      if (value === null) return { kind: 'unreadable' };
      return {
        kind: 'read',
        comparison: {
          op: sign?.[1] ?? bareOp(current),
          negated: sign?.[2] ?? false,
          values: [value],
          unit: one[2] ? one[2].toLowerCase() : null,
        },
      };
    }
    const range = sign === undefined ? NUMBER_RANGE.exec(rest) : null;
    if (range) {
      const low = numberOf(range[1]!);
      const high = numberOf(range[3]!);
      if (low === null || high === null) return { kind: 'unreadable' };
      const unit = range[4] ?? range[2];
      return {
        kind: 'read',
        comparison: {
          op: 'between',
          negated: false,
          values: low <= high ? [low, high] : [high, low],
          unit: unit ? unit.toLowerCase() : null,
        },
      };
    }
    return { kind: 'unreadable' };
  }

  const one = ONE_DAY.exec(rest);
  if (one) {
    const day = dayOf(one[1]!);
    if (day === null) return { kind: 'unreadable' };
    return {
      kind: 'read',
      comparison: { op: sign?.[1] ?? bareOp(current), negated: sign?.[2] ?? false, values: [day], unit: null },
    };
  }
  const range = sign === undefined ? DAY_RANGE.exec(rest) : null;
  if (range) {
    const low = dayOf(range[1]!);
    const high = dayOf(range[2]!);
    if (low === null || high === null) return { kind: 'unreadable' };
    return {
      kind: 'read',
      // Written as `YYYY-MM-DD`, the days order as text.
      comparison: { op: 'between', negated: false, values: low <= high ? [low, high] : [high, low], unit: null },
    };
  }
  return { kind: 'unreadable' };
}

/**
 * The condition a comparison is, in the shape a row holds.
 *
 * A day "equal to" is the whole of that day, from its first moment to its last, since a day field
 * compares moments and no moment is the day.
 */
export function conditionOf(comparison: Comparison, kind: TypedKind): TypedCondition {
  const { op, negated, values } = comparison;
  if (kind === 'number') {
    return { op, negated, values: values.map((value) => ({ type: 'number', value: value as number })) };
  }
  const days = values.map((value) => dayjs(value as string));
  if (op === 'equals') {
    return {
      op: 'between',
      negated,
      values: [
        { type: 'instant', value: dayBound('between', days[0]!, 'low') },
        { type: 'instant', value: dayBound('between', days[0]!, 'high') },
      ],
    };
  }
  if (op === 'between') {
    return {
      op,
      negated,
      values: [
        { type: 'instant', value: dayBound(op, days[0]!, 'low') },
        { type: 'instant', value: dayBound(op, days[1]!, 'high') },
      ],
    };
  }
  return { op, negated, values: [{ type: 'instant', value: dayBound(op, days[0]!, 'low') }] };
}

/** The day a stored bound was written for, as `YYYY-MM-DD`. */
function dayText(value: FilterValue | undefined): string | null {
  if (value?.type !== 'instant') return null;
  return pickedDay(value.value)?.format('YYYY-MM-DD') ?? null;
}

function numberText(value: FilterValue | undefined): string | null {
  return value?.type === 'number' && typeof value.value === 'number' ? String(value.value) : null;
}

/**
 * A row's condition as the text its box shows, or null when the row's values cannot be written as
 * one (unfinished, or not of this kind).
 *
 * A value under a single-value comparison is shown bare, since the comparison list beside the box
 * already names it; only what that list cannot say — "not equal to", a range, a whole day — is
 * spelled with signs. Each text read back with the same comparison chosen gives the same condition.
 */
export function formatCondition(condition: TypedCondition, kind: TypedKind): string | null {
  const { op, values, negated } = condition;
  const text = kind === 'number' ? numberText : dayText;
  if (op === 'between') {
    const low = text(values[0]);
    const high = text(values[1]);
    if (low === null || high === null) return null;
    if (kind === 'day' && low === high) {
      return negated ? `!=${low}` : low;
    }
    return `${low}..${high}`;
  }
  const value = text(values[0]);
  if (value === null) return null;
  return negated && op === 'equals' ? `!=${value}` : value;
}

const SIGN_OF: Record<ComparisonOp, string> = {
  equals: '=',
  greaterThan: '>',
  greaterOrEqual: '>=',
  lessThan: '<',
  lessOrEqual: '<=',
  between: '',
};

/**
 * A number comparison written out whole, with its sign, for a box that has no comparison list
 * beside it. The one comparison such a box means by a bare number is written bare, so a box that
 * means "at least" shows `300` for at least 300 and `>300` for more than 300.
 */
export function formatSigned(comparison: Pick<Comparison, 'op' | 'negated' | 'values'>, bare: ComparisonOp): string {
  const [first, second] = comparison.values.map(String);
  if (comparison.op === 'between') {
    return `${first}..${second}`;
  }
  if (comparison.negated) {
    return `!=${first}`;
  }
  return comparison.op === bare ? `${first}` : `${SIGN_OF[comparison.op]}${first}`;
}

/** Whether a row's negation is spelled in its box (`!=…`) rather than only by its "not" tick. */
export function negationShown(condition: TypedCondition, kind: TypedKind): boolean {
  return condition.negated && (formatCondition(condition, kind)?.startsWith('!=') ?? false);
}

/**
 * The row a typed comparison makes of the row it was typed into.
 *
 * A typed "equal to" or "not equal to" decides the negation, since the box spells it. Any other
 * comparison keeps a negation ticked by hand, and drops one only the box was spelling — so turning
 * `!=300` into `>300` asks "more than 300", not "not more than 300".
 */
export function resolveTyped(current: TypedCondition, comparison: Comparison, kind: TypedKind): TypedCondition {
  const next = conditionOf(comparison, kind);
  if (comparison.op === 'equals') {
    return next;
  }
  return { ...next, negated: current.negated && !negationShown(current, kind) };
}

/** What a comparison asks, as a translation key under `filters.typed` and its values. */
export function describeComparison(
  comparison: Comparison,
  kind: TypedKind,
): { key: string; values: Record<string, string> } {
  const unit = comparison.unit === null ? '' : ` ${comparison.unit}`;
  const shown = comparison.values.map((v) => `${String(v)}${unit}`);
  const prefix = kind === 'number' ? 'filters.typed.number' : 'filters.typed.day';
  if (comparison.op === 'between') {
    return { key: `${prefix}.between`, values: { low: shown[0]!, high: shown[1]! } };
  }
  const op = comparison.op === 'equals' && comparison.negated ? 'notEquals' : comparison.op;
  return { key: `${prefix}.${op}`, values: { value: shown[0]! } };
}
