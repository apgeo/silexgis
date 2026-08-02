// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { ProfileOutlined } from '@ant-design/icons';
import { App, Button, Checkbox, Flex, Input, InputNumber, Popover, Select, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import { useDocument, useDocumentTypes, useUpdateDocument } from '../../api/hooks.ts';
import { parsePropertiesSchema, type SchemaField } from '../typedProperties/propertiesSchema.ts';

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <Flex vertical gap={2}>
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        {label}
      </Typography.Text>
      {children}
    </Flex>
  );
}

/**
 * Edit the document behind a file: its title, its kind, and the metadata that kind
 * describes. These belong to the document rather than to the bytes, so they survive a new
 * version — which is exactly why the file response carries a document id to address.
 *
 * The form is built from the kind's JSON schema, so adding a field to a kind needs no
 * client change. Values the current schema does not know about are carried through
 * untouched: a kind that drops a field must not silently erase what was written under the
 * previous one.
 */
export default function DocumentMetadata({ documentId }: { documentId: string }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [open, setOpen] = useState(false);
  const { data: document } = useDocument(documentId, open);
  const { data: types } = useDocumentTypes();
  const updateDocument = useUpdateDocument();

  const [title, setTitle] = useState('');
  const [typeId, setTypeId] = useState<number | null>(null);
  const [values, setValues] = useState<Record<string, unknown>>({});

  // Re-sync from server state whenever the popover opens, or when the fetch that the
  // opening started arrives — another edit may have landed since it was last read.
  useEffect(() => {
    if (open && document) {
      setTitle(document.title);
      setTypeId(document.documentTypeId);
      setValues((document.metadata as Record<string, unknown> | null) ?? {});
    }
  }, [open, document]);

  const selectedType = types?.find((type) => type.id === typeId);
  const fields = useMemo(
    () => parsePropertiesSchema(selectedType?.metadataSchema),
    [selectedType],
  );

  const save = async () => {
    // Merge the schema-driven values over what is stored so keys the current schema does
    // not know about survive the round trip; an emptied field is removed rather than
    // written as a blank, so "not filled in" and "filled in with nothing" stay distinct.
    const metadata: Record<string, unknown> = { ...values };
    for (const field of fields) {
      const value = values[field.key];
      if (value === undefined || value === null || value === '') {
        delete metadata[field.key];
      } else {
        metadata[field.key] = value;
      }
    }

    try {
      await updateDocument.mutateAsync({
        id: documentId,
        title: title.trim(),
        documentTypeId: typeId,
        metadata,
      });
      message.success(t('common.saved'));
      setOpen(false);
    } catch (error) {
      // The server checks the metadata against the kind's schema, and that refusal is the
      // one worth naming: "save failed" would leave someone guessing which field it meant.
      message.error(
        error instanceof ApiError && error.code === 'document.metadata_invalid'
          ? t('documents.metadataInvalid')
          : t('common.saveFailed'),
      );
    }
  };

  const content = (
    <Flex vertical gap={10} style={{ width: 300 }}>
      <Field label={t('documents.title')}>
        <Input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={300} />
      </Field>
      <Field label={t('documents.type')}>
        <Select
          value={typeId}
          onChange={setTypeId}
          allowClear
          placeholder={t('documents.noType')}
          onClear={() => setTypeId(null)}
          options={(types ?? []).map((type) => ({ value: type.id, label: type.name }))}
        />
      </Field>
      {fields.map((field) => (
        <Field key={field.key} label={field.required ? `${field.label} *` : field.label}>
          <TypedField
            field={field}
            value={values[field.key]}
            onChange={(value) => setValues((current) => ({ ...current, [field.key]: value }))}
          />
        </Field>
      ))}
      <Button
        type="primary"
        size="small"
        disabled={!document || !title.trim()}
        loading={updateDocument.isPending}
        onClick={() => void save()}
      >
        {t('common.save')}
      </Button>
    </Flex>
  );

  return (
    <Popover
      content={content}
      title={t('documents.metadata')}
      trigger="click"
      open={open}
      onOpenChange={setOpen}
    >
      <Button size="small" type="text" icon={<ProfileOutlined />} aria-label={t('documents.metadata')} />
    </Popover>
  );
}

/** One schema-described value, as the control its declared type calls for. */
function TypedField({
  field,
  value,
  onChange,
}: {
  field: SchemaField;
  value: unknown;
  onChange: (value: unknown) => void;
}) {
  switch (field.kind) {
    case 'boolean':
      return <Checkbox checked={value === true} onChange={(e) => onChange(e.target.checked)} />;
    case 'enum':
      return (
        <Select
          value={typeof value === 'string' ? value : undefined}
          onChange={onChange}
          allowClear
          onClear={() => onChange(undefined)}
          options={(field.enumValues ?? []).map((option) => ({ value: option, label: option }))}
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
        />
      );
    default:
      return (
        <Input
          value={typeof value === 'string' ? value : ''}
          onChange={(e) => onChange(e.target.value)}
          maxLength={2000}
        />
      );
  }
}
