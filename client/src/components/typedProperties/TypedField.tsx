// SPDX-License-Identifier: AGPL-3.0-or-later
import { Checkbox, Input, InputNumber, Select } from 'antd';
import type { SchemaField } from './propertiesSchema.ts';

/**
 * One schema-described value, as the control its declared type calls for.
 *
 * Which control a declared kind calls for is one piece of knowledge, so it lives beside the
 * parser that decides what the kinds are rather than once per surface that draws a typed bag.
 * Uncontrolled by antd's Form on purpose: the surfaces that carry these bags hold the whole
 * stored object in state so keys the schema does not know about survive a write, and a form
 * field only ever knows about the keys the schema declares.
 */
export default function TypedField({
  field,
  value,
  onChange,
  optionLabel,
  'data-testid': testId,
}: {
  field: SchemaField;
  value: unknown;
  onChange: (value: unknown) => void;
  /**
   * How a choice's stored value is worded on screen. A value is a code, and a surface whose
   * codes ship with the product words them in the reader's language; without one the value is
   * shown as it is stored, which is all an installation's own choice has.
   */
  optionLabel?: (value: string) => string;
  'data-testid'?: string;
}) {
  switch (field.kind) {
    case 'boolean':
      return (
        <Checkbox
          checked={value === true}
          onChange={(e) => onChange(e.target.checked)}
          data-testid={testId}
        />
      );
    case 'enum':
      return (
        <Select
          value={typeof value === 'string' ? value : undefined}
          onChange={onChange}
          allowClear
          onClear={() => onChange(undefined)}
          options={(field.enumValues ?? []).map((option) => ({
            value: option,
            label: optionLabel ? optionLabel(option) : option,
          }))}
          data-testid={testId}
        />
      );
    case 'number':
    case 'integer':
      return (
        <InputNumber
          value={typeof value === 'number' ? value : null}
          onChange={(next) => onChange(next ?? undefined)}
          min={field.min}
          max={field.max}
          precision={field.kind === 'integer' ? 0 : undefined}
          style={{ width: '100%' }}
          data-testid={testId}
        />
      );
    default:
      return (
        <Input
          value={typeof value === 'string' ? value : ''}
          onChange={(e) => onChange(e.target.value)}
          maxLength={2000}
          data-testid={testId}
        />
      );
  }
}
