// SPDX-License-Identifier: AGPL-3.0-or-later
import { Button, Checkbox, Flex, Input, InputNumber, Select } from 'antd';
import { useTranslation } from 'react-i18next';
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
  const { t } = useTranslation();
  switch (field.kind) {
    case 'boolean': {
      // Three states, not two. A question nobody has answered is a different fact from one
      // answered "no" — a plan whose permit question is unanswered is not a plan that needs no
      // permit — and a bare checkbox can only ever produce the second of the two once it has
      // been touched. So an unanswered question is drawn as neither ticked nor unticked, and
      // there is a way back to it: without one, a mistaken tick is a decision the record can
      // never unmake, and whatever reads the bag later would read it as a real answer.
      const answered = typeof value === 'boolean';
      return (
        <Flex align="center" gap={8}>
          <Checkbox
            checked={value === true}
            indeterminate={!answered}
            onChange={(e) => onChange(e.target.checked)}
            data-testid={testId}
          />
          {answered && (
            <Button
              type="link"
              size="small"
              onClick={() => onChange(undefined)}
              data-testid={testId ? `${testId}-clear` : undefined}
            >
              {t('common.clearAnswer')}
            </Button>
          )}
        </Flex>
      );
    }
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
