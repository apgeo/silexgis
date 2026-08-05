// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { ProfileOutlined } from '@ant-design/icons';
import { App, Button, Checkbox, Flex, Input, InputNumber, Popover, Select, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  hasAccessAction,
  useCabinets,
  useCapabilities,
  useCavingGroups,
  useDocument,
  useDocumentTypes,
  useFileDocument,
  useUpdateDocument,
  type CabinetInfo,
  type Visibility,
} from '../../api/hooks.ts';
import TextState from '../documents/TextState.tsx';
import { parsePropertiesSchema, type SchemaField } from '../typedProperties/propertiesSchema.ts';

const visibilities: Visibility[] = ['private', 'cavingGroup', 'authenticated', 'public'];

/**
 * The languages this installation indexes with a stemmer of their own. Anything else — and
 * "nobody has said" — indexes language-neutrally, which is what clearing the field asks for,
 * so the list is a list of the choices that change something rather than of world languages.
 */
const languages = ['ro', 'en'] as const;

/**
 * A cabinet named by its whole path. Names are unique only among siblings — "1987" sits
 * under many archives — so a bare name would be ambiguous away from the tree that gives
 * it context, which is exactly the situation here.
 */
function cabinetOptions(cabinets: CabinetInfo[]) {
  const names = new Map(cabinets.map((cabinet) => [cabinet.id, cabinet.name]));
  return cabinets
    .map((cabinet) => ({
      value: cabinet.id,
      label: cabinet.ancestorIds.map((id) => names.get(id) ?? '…').join(' / '),
    }))
    .sort((a, b) => a.label.localeCompare(b.label));
}

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
  const { data: cavingGroups } = useCavingGroups();
  const updateDocument = useUpdateDocument();

  // Filing is edited here as well as from the tree, because the tree can only re-file
  // what is already on a shelf — a document has to be able to reach its first cabinet
  // from the panel that owns it. Whoever may not write documents at all never sees the
  // control; the server re-decides it per cabinet, so this only hides what it would refuse.
  const { data: capabilities } = useCapabilities();
  const mayFile = hasAccessAction(capabilities?.domains.documents, 'write');
  const { data: cabinets } = useCabinets(open && mayFile);
  const fileDocument = useFileDocument();

  const [title, setTitle] = useState('');
  const [typeId, setTypeId] = useState<number | null>(null);
  const [visibility, setVisibility] = useState<Visibility>('private');
  const [cavingGroupId, setCavingGroupId] = useState<string | null>(null);
  const [language, setLanguage] = useState<string | null>(null);
  const [filedIn, setFiledIn] = useState<string[]>([]);
  const [values, setValues] = useState<Record<string, unknown>>({});

  // Re-sync from server state whenever the popover opens, or when the fetch that the
  // opening started arrives — another edit may have landed since it was last read.
  useEffect(() => {
    if (open && document) {
      setTitle(document.title);
      setTypeId(document.documentTypeId);
      setVisibility(document.visibility);
      setCavingGroupId(document.cavingGroupId);
      setLanguage(document.language);
      setFiledIn(document.cabinetIds);
      setValues((document.metadata as Record<string, unknown> | null) ?? {});
    }
  }, [open, document]);

  const selectedType = types?.find((type) => type.id === typeId);
  const fields = useMemo(
    () => parsePropertiesSchema(selectedType?.metadataSchema),
    [selectedType],
  );
  const shelves = useMemo(() => cabinetOptions(cabinets ?? []), [cabinets]);

  /**
   * Filing is its own request rather than part of Save: it is guarded separately (write on
   * the document *and* a right at that cabinet), so bundling it into the form would make a
   * refusal of one look like a refusal of the other, and could half-succeed.
   */
  const file = async (cabinetId: string, filed: boolean) => {
    const before = filedIn;
    setFiledIn(filed ? [...before, cabinetId] : before.filter((id) => id !== cabinetId));
    try {
      await fileDocument.mutateAsync({ cabinetId, documentId, filed });
      message.success(t('common.saved'));
    } catch (error) {
      setFiledIn(before);
      message.error(
        error instanceof ApiError && error.code === 'document.write_forbidden'
          ? t('cabinets.filingForbidden')
          : t('common.saveFailed'),
      );
    }
  };

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
        visibility,
        // A club binding only means anything under club visibility; keeping a stale one
        // on a document turned private would leave the club named on a row it no longer
        // decides anything about.
        cavingGroupId: visibility === 'cavingGroup' ? cavingGroupId : null,
        // An empty string clears the code; null would mean "I am not talking about the
        // language", which is not what a person who just emptied the control meant.
        language: language ?? '',
      });
      message.success(t('common.saved'));
      setOpen(false);
    } catch (error) {
      // Two refusals are worth naming rather than folding into "save failed": metadata the
      // kind's schema rejects, and binding to a club the saver does not belong to — both
      // leave someone guessing which control was at fault otherwise.
      message.error(
        error instanceof ApiError && error.code === 'document.metadata_invalid'
          ? t('documents.metadataInvalid')
          : error instanceof ApiError && error.code === 'access.caving_group_binding_forbidden'
            ? t('documents.bindingForbidden')
            : t('common.saveFailed'),
      );
    }
  };

  const content = (
    <Flex vertical gap={10} style={{ width: 300 }}>
      {document && (
        <Field label={t('documents.text')}>
          <TextState state={document.textExtraction} />
        </Field>
      )}
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
      <Field label={t('documents.visibility')}>
        <Select
          value={visibility}
          onChange={setVisibility}
          options={visibilities.map((value) => ({
            value,
            label: t(`caves.visibilityValues.${value}`),
          }))}
        />
      </Field>
      <Field label={t('documents.language')}>
        <Select
          value={language}
          onChange={setLanguage}
          allowClear
          onClear={() => setLanguage(null)}
          placeholder={t('documents.languageUnknown')}
          options={languages.map((code) => ({
            value: code,
            label: t(`documents.languages.${code}`),
          }))}
        />
      </Field>
      {visibility === 'cavingGroup' && (
        <Field label={t('documents.cavingGroup')}>
          <Select
            value={cavingGroupId}
            onChange={setCavingGroupId}
            showSearch
            optionFilterProp="label"
            placeholder={t('documents.pickCavingGroup')}
            options={(cavingGroups ?? []).map((group) => ({ value: group.id, label: group.name }))}
          />
        </Field>
      )}
      {mayFile && shelves.length > 0 && (
        <Field label={t('documents.cabinets')}>
          <Select
            mode="multiple"
            value={filedIn}
            showSearch
            optionFilterProp="label"
            placeholder={t('documents.pickCabinets')}
            disabled={fileDocument.isPending}
            onSelect={(id: string) => void file(id, true)}
            onDeselect={(id: string) => void file(id, false)}
            options={shelves}
          />
          <Typography.Text type="secondary" style={{ fontSize: 11 }}>
            {t('documents.filingMovesAccess')}
          </Typography.Text>
        </Field>
      )}
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
        disabled={
          !document || !title.trim() || (visibility === 'cavingGroup' && !cavingGroupId)
        }
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
