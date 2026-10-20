// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { DatePicker, Flex, Input, Select, Typography } from 'antd';
import type { Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { dayBound, pickedDay } from '../../filters/dayBounds.ts';
import { editableOps } from '../../filters/editorTree.ts';
import {
  describeComparison,
  formatCondition,
  negationShown,
  readComparison,
  resolveTyped,
  type TypedCondition,
  type TypedKind,
} from '../../filters/typedComparison.ts';
import type { FilterField, FilterOp, FilterValue } from '../../filters/types.ts';
import IdPicker from './IdPicker.tsx';
import SpatialValueInput from './SpatialValueInput.tsx';

/**
 * The value part of one condition row, shaped by the field's kind and the operator.
 *
 * A value the person has not finished — an empty box, one end of a range — is held as a missing or
 * null value, which the document builders treat as "not written yet" and leave out of what is
 * counted and applied. Nothing here invents a default, because a default nobody typed is a
 * condition nobody asked.
 */

export interface ValueInputProps {
  /** The world the row filters — what a spatial row's object anchor is picked from. */
  world: string;
  field: FilterField;
  op: FilterOp;
  values: FilterValue[];
  onChange: (values: FilterValue[]) => void;
  /** Whether the row is negated: a typed "not equal to" is spelled in the box. */
  negated: boolean;
  /**
   * A comparison typed with signs, which may change the row's comparison and negation as well as
   * its values.
   */
  onTyped: (next: TypedCondition) => void;
  testId: string;
}

/** A range end not written yet: kept in its position, and incomplete until it is written. */
function blank(type: 'number' | 'instant'): FilterValue {
  return { type, value: null } as unknown as FilterValue;
}

function instantAt(values: FilterValue[], index: number): Dayjs | null {
  const v = values[index];
  return v?.type === 'instant' && typeof v.value === 'string' ? pickedDay(v.value) : null;
}

export default function ValueInput({ world, field, op, values, onChange, negated, onTyped, testId }: ValueInputProps) {
  const { t } = useTranslation();
  const label = t('filters.editor.value');

  if (op === 'isEmpty' || op === 'isNotEmpty') {
    return null;
  }

  switch (field.kind) {
    case 'text':
      return (
        <Input
          aria-label={label}
          data-testid={testId}
          value={values[0]?.type === 'text' ? values[0].value : ''}
          onChange={(e) => onChange(e.target.value === '' ? [] : [{ type: 'text', value: e.target.value }])}
          style={{ width: 200 }}
        />
      );

    case 'number':
      return (
        <TypedComparisonInput kind="number" field={field} condition={{ op, values, negated }}
          onTyped={onTyped} testId={testId} ariaLabel={label} placeholder={t('filters.typed.hint')} />
      );

    case 'boolean':
      return (
        <Select
          aria-label={label}
          data-testid={testId}
          value={values[0]?.type === 'boolean' ? String(values[0].value) : undefined}
          onChange={(v: string) => onChange([{ type: 'boolean', value: v === 'true' }])}
          options={[
            { value: 'true', label: t('filters.editor.yes') },
            { value: 'false', label: t('filters.editor.no') },
          ]}
          style={{ width: 120 }}
        />
      );

    case 'instant': {
      // The picker for choosing a day by eye, and beside it a box for typing one with a sign.
      const picker = op === 'between'
        ? (
          <DatePicker.RangePicker
            aria-label={label}
            data-testid={testId}
            value={[instantAt(values, 0), instantAt(values, 1)]}
            allowEmpty={[true, true]}
            onChange={(range) => onChange([
              range?.[0] ? { type: 'instant', value: dayBound(op, range[0], 'low') } : blank('instant'),
              range?.[1] ? { type: 'instant', value: dayBound(op, range[1], 'high') } : blank('instant'),
            ])}
          />
        )
        : (
          <DatePicker
            aria-label={label}
            data-testid={testId}
            value={instantAt(values, 0)}
            onChange={(day: Dayjs | null) =>
              onChange(day ? [{ type: 'instant', value: dayBound(op, day, 'low') }] : [])}
          />
        );
      return (
        <TypedComparisonInput kind="day" field={field} condition={{ op, values, negated }}
          onTyped={onTyped} testId={`${testId}-typed`} ariaLabel={t('filters.typed.hintDay')}
          placeholder={t('filters.typed.hintDay')} before={picker} />
      );
    }

    case 'id':
      return (
        <IdPicker
          options={field.options}
          multiple={op === 'in'}
          value={values.filter((v) => v.type === 'id').map((v) => v.value)}
          onChange={(ids) => onChange(ids.map((value) => ({ type: 'id', value })))}
          ariaLabel={label}
          testId={testId}
        />
      );

    case 'spatial':
      return <SpatialValueInput world={world} op={op} values={values} onChange={onChange} testId={testId} />;

    default:
      return null;
  }
}

/** A condition's identity, whatever order its members were written in. */
function keyOf(condition: TypedCondition): string {
  return JSON.stringify([condition.op, condition.negated, condition.values.map((v) => [v.type, 'value' in v ? v.value : null])]);
}

interface TypedComparisonInputProps {
  kind: TypedKind;
  field: FilterField;
  condition: TypedCondition;
  onTyped: (next: TypedCondition) => void;
  testId: string;
  ariaLabel: string;
  placeholder: string;
  /** A control drawn ahead of the box, in the same line. */
  before?: ReactNode;
}

/**
 * A box a comparison is typed into, with what was understood beside it.
 *
 * The box keeps what was typed; the row keeps the condition it was read as. Text that cannot be
 * read, or that asks a comparison this field does not offer, leaves the row without a value, so it
 * is left out of what is counted and applied, and the box says why. A change that arrives from
 * outside — another comparison chosen, the "not" ticked, a day picked — writes the row's condition
 * back into the box.
 */
function TypedComparisonInput({
  kind, field, condition, onTyped, testId, ariaLabel, placeholder, before,
}: TypedComparisonInputProps) {
  const { t } = useTranslation();
  const key = keyOf(condition);
  const shown = formatCondition(condition, kind) ?? '';
  const [text, setText] = useState(shown);
  const written = useRef(key);
  useEffect(() => {
    if (written.current !== key) {
      written.current = key;
      setText(shown);
    }
  }, [key, shown]);

  const reading = readComparison(text, kind, condition.op);
  const next = reading.kind === 'read' ? resolveTyped(condition, reading.comparison, kind) : null;
  const offered = next !== null && editableOps(field).includes(next.op);

  const type = (typed: string) => {
    setText(typed);
    const read = readComparison(typed, kind, condition.op);
    const resolved = read.kind === 'read' ? resolveTyped(condition, read.comparison, kind) : null;
    const out: TypedCondition = resolved !== null && editableOps(field).includes(resolved.op)
      ? resolved
      : { op: condition.op, values: [], negated: condition.negated && !negationShown(condition, kind) };
    written.current = keyOf(out);
    onTyped(out);
  };

  const refused = reading.kind === 'unreadable' || (reading.kind === 'read' && !offered);
  let note: ReactNode = null;
  if (refused) {
    const message = reading.kind === 'unreadable'
      ? t(kind === 'number' ? 'filters.typed.unreadable' : 'filters.typed.unreadableDay')
      : t('filters.typed.notOffered');
    note = (
      <Typography.Text type="danger" role="alert" data-testid={`${testId}-unreadable`}>
        {message}
      </Typography.Text>
    );
  } else if (reading.kind === 'read') {
    const described = describeComparison(reading.comparison, kind);
    note = (
      <Typography.Text type="secondary" aria-live="polite" data-testid={`${testId}-understood`}>
        {t('filters.typed.understood', { text: t(described.key, described.values) })}
      </Typography.Text>
    );
  }

  return (
    <Flex gap={8} wrap align="center">
      {before}
      <Input
        aria-label={ariaLabel}
        data-testid={testId}
        placeholder={placeholder}
        value={text}
        onChange={(e) => type(e.target.value)}
        status={refused ? 'error' : undefined}
        style={{ width: kind === 'number' ? 160 : 200 }}
      />
      {note}
    </Flex>
  );
}
