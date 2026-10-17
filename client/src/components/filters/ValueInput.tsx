// SPDX-License-Identifier: AGPL-3.0-or-later
import { DatePicker, Input, InputNumber, Select, Space } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
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
  testId: string;
}

/** A range end not written yet: kept in its position, and incomplete until it is written. */
function blank(type: 'number' | 'instant'): FilterValue {
  return { type, value: null } as unknown as FilterValue;
}

function numberAt(values: FilterValue[], index: number): number | null {
  const v = values[index];
  return v?.type === 'number' && typeof v.value === 'number' ? v.value : null;
}

function instantAt(values: FilterValue[], index: number): Dayjs | null {
  const v = values[index];
  return v?.type === 'instant' && typeof v.value === 'string' ? dayjs(v.value) : null;
}

/**
 * Which moment of a chosen day a comparison means. "After the 3rd" is after the 3rd has ended and
 * "before the 3rd" is before it began, while "at least the 3rd" takes the whole of the 3rd in and
 * "at most the 3rd" does too — so a day picked here means the day, under every operator.
 */
function instantFor(op: FilterOp, day: Dayjs, end: 'low' | 'high'): string {
  const late = op === 'greaterThan' || op === 'lessOrEqual' || (op === 'between' && end === 'high');
  return (late ? day.endOf('day') : day.startOf('day')).toISOString();
}

export default function ValueInput({ world, field, op, values, onChange, testId }: ValueInputProps) {
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

    case 'number': {
      const put = (index: number, next: number | null) => {
        if (op !== 'between') {
          onChange(next === null ? [] : [{ type: 'number', value: next }]);
          return;
        }
        const pair = [values[0] ?? blank('number'), values[1] ?? blank('number')];
        pair[index] = next === null ? blank('number') : { type: 'number', value: next };
        onChange(pair);
      };
      if (op === 'between') {
        return (
          <Space.Compact>
            <InputNumber aria-label={t('filters.editor.from')} data-testid={`${testId}-low`}
              value={numberAt(values, 0)} onChange={(n) => put(0, n)} />
            <InputNumber aria-label={t('filters.editor.to')} data-testid={`${testId}-high`}
              value={numberAt(values, 1)} onChange={(n) => put(1, n)} />
          </Space.Compact>
        );
      }
      return (
        <InputNumber aria-label={label} data-testid={testId}
          value={numberAt(values, 0)} onChange={(n) => put(0, n)} />
      );
    }

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

    case 'instant':
      if (op === 'between') {
        return (
          <DatePicker.RangePicker
            aria-label={label}
            data-testid={testId}
            value={[instantAt(values, 0), instantAt(values, 1)]}
            allowEmpty={[true, true]}
            onChange={(range) => onChange([
              range?.[0] ? { type: 'instant', value: instantFor(op, range[0], 'low') } : blank('instant'),
              range?.[1] ? { type: 'instant', value: instantFor(op, range[1], 'high') } : blank('instant'),
            ])}
          />
        );
      }
      return (
        <DatePicker
          aria-label={label}
          data-testid={testId}
          value={instantAt(values, 0)}
          onChange={(day: Dayjs | null) =>
            onChange(day ? [{ type: 'instant', value: instantFor(op, day, 'low') }] : [])}
        />
      );

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
