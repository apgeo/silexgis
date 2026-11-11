// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Collapse, Empty, Flex, Input, InputNumber, Select, Switch, Tag, Typography, theme } from 'antd';
import { UndoOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import Button from '../Button.tsx';
import {
  fieldDiffers,
  fieldValue,
  listText,
  parseList,
  withValueAt,
  type ParameterField,
  type ParameterGroup,
} from './parameterSchema.ts';

interface ProfileParameterFormProps {
  groups: readonly ParameterGroup[];
  values: unknown;
  onChange?: (values: Record<string, unknown>) => void;
  /** A built-in, or a profile the caller may not change: every setting shown, none editable. */
  readOnly?: boolean;
  /** Show only the settings that differ from the defaults. */
  onlyChanged?: boolean;
}

/**
 * A step's settings as a form generated from its schema, one collapsible group per section.
 *
 * Every setting shows its default next to it, and one that differs from it is marked and can be
 * put back with one press. A setting's name is the tool's own word (HDMapping's TOML key, say) and
 * is shown as written, because that is the name its documentation and its desktop program use;
 * what the schema says about it, when it says anything, is the help line under it.
 */
export default function ProfileParameterForm({
  groups,
  values,
  onChange,
  readOnly = false,
  onlyChanged = false,
}: ProfileParameterFormProps) {
  const { t } = useTranslation();
  const shown = groups
    .map((group) => ({
      group,
      fields: onlyChanged ? group.fields.filter((f) => fieldDiffers(f, values)) : group.fields,
    }))
    .filter(({ fields }) => fields.length > 0);

  if (shown.length === 0) {
    return (
      <Empty
        image={Empty.PRESENTED_IMAGE_SIMPLE}
        description={onlyChanged ? t('settings.scanProfiles.form.nothingChanged') : t('settings.scanProfiles.form.noSettings')}
        data-testid="profile-form-empty"
      />
    );
  }

  const set = (field: ParameterField, value: unknown) => onChange?.(withValueAt(values, field.path, value));

  return (
    <Collapse
      data-testid="profile-form"
      // The first section open, so a flat step's few settings are on screen without a press.
      defaultActiveKey={[shown[0]!.group.id]}
      items={shown.map(({ group, fields }) => {
        const changed = group.fields.filter((f) => fieldDiffers(f, values)).length;
        return {
          key: group.id,
          label: (
            <Flex gap={8} align="center" wrap>
              <Typography.Text strong code={group.title !== ''}>
                {group.title === '' ? t('settings.scanProfiles.form.general') : group.title}
              </Typography.Text>
              {changed > 0 && (
                <Tag color="gold" data-testid="profile-group-changed">
                  {t('settings.scanProfiles.form.changedCount', { count: changed })}
                </Tag>
              )}
            </Flex>
          ),
          children: (
            <Flex vertical gap={12}>
              {fields.map((field) => (
                <FieldRow
                  key={field.path.join('.')}
                  field={field}
                  values={values}
                  readOnly={readOnly}
                  onSet={(value) => set(field, value)}
                />
              ))}
            </Flex>
          ),
        };
      })}
    />
  );
}

function shownValue(value: unknown): string {
  if (value === undefined || value === null) return '—';
  if (Array.isArray(value)) return listText(value);
  return String(value);
}

function FieldRow({
  field,
  values,
  readOnly,
  onSet,
}: {
  field: ParameterField;
  values: unknown;
  readOnly: boolean;
  onSet: (value: unknown) => void;
}) {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const differs = fieldDiffers(field, values);
  const value = fieldValue(field, values);
  const id = `profile-field-${field.path.join('-')}`;

  return (
    <div
      data-testid="profile-field"
      data-key={field.path.join('.')}
      data-changed={differs ? 'true' : 'false'}
      style={{
        padding: '6px 8px',
        borderRadius: token.borderRadius,
        background: differs ? token.colorWarningBg : undefined,
        borderInlineStart: `3px solid ${differs ? token.colorWarning : 'transparent'}`,
      }}
    >
      <Flex justify="space-between" align="center" gap={12} wrap>
        <Flex vertical style={{ minWidth: 0, flex: '1 1 220px' }}>
          <label htmlFor={id}>
            <Typography.Text code>{field.key}</Typography.Text>
          </label>
          {field.description && <Typography.Text type="secondary">{field.description}</Typography.Text>}
          <Typography.Text type="secondary" style={{ fontSize: token.fontSizeSM }}>
            {t('settings.scanProfiles.form.defaultIs', { value: shownValue(field.defaultValue) })}
          </Typography.Text>
        </Flex>
        <Flex gap={4} align="center">
          <FieldControl id={id} field={field} value={value} readOnly={readOnly} onSet={onSet} />
          {differs && !readOnly && (
            <Button
              size="small"
              type="text"
              icon={<UndoOutlined />}
              aria-label={t('settings.scanProfiles.form.reset', { key: field.key })}
              onClick={() => onSet(field.defaultValue)}
            />
          )}
        </Flex>
      </Flex>
    </div>
  );
}

function FieldControl({
  id,
  field,
  value,
  readOnly,
  onSet,
}: {
  id: string;
  field: ParameterField;
  value: unknown;
  readOnly: boolean;
  onSet: (value: unknown) => void;
}) {
  switch (field.kind) {
    case 'boolean':
      return <Switch id={id} checked={value === true} disabled={readOnly} onChange={(checked) => onSet(checked)} />;
    case 'integer':
    case 'number': {
      // An exclusive bound is offered as the inclusive one the control knows; the server's check
      // is the one that refuses the bound itself.
      const min = field.minimum ?? field.exclusiveMinimum;
      const max = field.maximum ?? field.exclusiveMaximum;
      return (
        <InputNumber
          id={id}
          value={typeof value === 'number' ? value : null}
          min={min}
          max={max}
          precision={field.kind === 'integer' ? 0 : undefined}
          step={field.kind === 'integer' ? 1 : undefined}
          disabled={readOnly}
          style={{ width: 160 }}
          onChange={(next) => {
            if (typeof next === 'number' && Number.isFinite(next)) onSet(next);
          }}
        />
      );
    }
    case 'choice':
      return (
        <Select
          id={id}
          value={typeof value === 'string' ? value : undefined}
          options={(field.options ?? []).map((option) => ({ value: option, label: option }))}
          disabled={readOnly}
          style={{ width: 160 }}
          onChange={(next) => onSet(next)}
        />
      );
    case 'numberList':
    case 'textList':
      return <ListInput id={id} kind={field.kind} value={value} readOnly={readOnly} onSet={onSet} />;
    case 'fixed':
      return <Typography.Text id={id}>{shownValue(value)}</Typography.Text>;
    default:
      return (
        <Input
          id={id}
          value={typeof value === 'string' ? value : ''}
          disabled={readOnly}
          style={{ width: 160 }}
          onChange={(event) => onSet(event.target.value)}
        />
      );
  }
}

/**
 * A list typed as comma-separated items. What was typed is kept while it does not read as a list
 * of the right kind, and only a list that does is written back, so a half-typed value never
 * replaces a good one.
 */
function ListInput({
  id,
  kind,
  value,
  readOnly,
  onSet,
}: {
  id: string;
  kind: 'numberList' | 'textList';
  value: unknown;
  readOnly: boolean;
  onSet: (value: unknown) => void;
}) {
  const { t } = useTranslation();
  const [draft, setDraft] = useState<string | null>(null);
  const text = draft ?? listText(value);
  const invalid = draft !== null && parseList(draft, kind) === null;
  return (
    <Input
      id={id}
      value={text}
      disabled={readOnly}
      status={invalid ? 'error' : undefined}
      title={invalid ? t('settings.scanProfiles.form.listInvalid') : undefined}
      style={{ width: 200 }}
      onChange={(event) => {
        const next = event.target.value;
        setDraft(next);
        const parsed = parseList(next, kind);
        if (parsed !== null) onSet(parsed);
      }}
      onBlur={() => {
        if (!invalid) setDraft(null);
      }}
    />
  );
}
