// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { CloseOutlined, FilterOutlined, SaveOutlined, SettingOutlined } from '@ant-design/icons';
import { Alert, App, Drawer, Flex, Form, Input, Modal, Select, Tag, Tooltip } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import {
  appliesTo,
  documentOf,
  useCreateSavedFilter,
  useSavedFilters,
  type SavedFilter,
} from '../../api/hooks/savedFilters.ts';
import {
  readWorldDocument,
  sameWorldQuestion,
  worldDocument,
  worldEditorDocument,
} from '../../filters/worldList.ts';
import { audienceFields, type SavedFilterAudience } from '../../filters/savedFilterAudience.ts';
import type { FilterNode } from '../../filters/types.ts';
import Button from '../Button.tsx';
import FilterEditor from './FilterEditor.tsx';
import SavedFilterAudienceField from './SavedFilterAudienceField.tsx';
import { savedFilterSaveFailedKey, useSavedFilterAudienceChoice } from './useSavedFilterAudienceChoice.ts';

interface Props {
  /** The filter world the list is about (`geofile`, `georeferencedMap`). */
  world: string;
  /** Prefix of every test id, so two lists on one screen stay apart. */
  testIdPrefix: string;
  /** Translation key of the editor's title. */
  titleKey: string;
  /** The world's conditions the list is asked with now. */
  where: FilterNode | null;
  /** The saved filter the conditions were taken from, if any. */
  savedFilterId?: string;
  /** A filter in the address could not be read and was left out. */
  unreadableLink?: boolean;
  /** The server refused the conditions the list was asked with. */
  refused?: boolean;
  onChange: (where: FilterNode | null, savedFilterId: string | undefined) => void;
}

/**
 * The condition editor and saved filters of a list screen, as the gallery has them: a button that
 * opens the editor on the conditions in force, a saved filter to apply by name, the conditions on
 * screen to save as a new one, and a tag saying which saved filter they came from and whether they
 * have moved since.
 *
 * Applying a saved filter only sets the conditions. The list is then asked with them under this
 * account's own access, so a filter somebody shared shows exactly the rows this person could have
 * found by writing the same conditions — a position condition is decided on the file's footprint,
 * and a map linked to a cave whose place this account may not see exactly is left out and not counted.
 */
export default function WorldFilterBar({
  world,
  testIdPrefix,
  titleKey,
  where,
  savedFilterId,
  unreadableLink = false,
  refused = false,
  onChange,
}: Props) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const { data: savedFilters, isSuccess } = useSavedFilters();
  const create = useCreateSavedFilter();
  const [open, setOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [form] = Form.useForm<{ name: string; description?: string; audience: SavedFilterAudience }>();
  const { groups, otherGroups, defaultAudience } = useSavedFilterAudienceChoice();

  const document = worldDocument(world, where);
  const readableAs = (filter: SavedFilter): FilterNode | null | undefined => {
    const saved = documentOf(filter);
    return filter.state === 'unreadable' || saved === null ? undefined : readWorldDocument(saved, world);
  };
  const active = savedFilterId === undefined ? undefined : savedFilters?.find((f) => f.id === savedFilterId);
  const activeWhere = active ? readableAs(active) : undefined;
  const changed = activeWhere !== undefined && !sameWorldQuestion(activeWhere, where);

  const apply = (id: string) => {
    const filter = savedFilters?.find((candidate) => candidate.id === id);
    if (!filter) return;
    if (filter.state === 'unreadable') {
      message.warning(t('worldFilter.savedUnreadable'));
      return;
    }
    const read = readableAs(filter);
    if (read === undefined) {
      message.warning(t('worldFilter.savedNotAboutThis'));
      return;
    }
    onChange(read, filter.id);
  };

  const save = async () => {
    if (document === null) {
      message.warning(t('savedFilters.nothingToSave'));
      return;
    }
    const values = await form.validateFields();
    try {
      const created = await create.mutateAsync({
        name: values.name.trim(),
        description: values.description?.trim() || null,
        document,
        ...audienceFields(values.audience),
      });
      setSaving(false);
      form.resetFields();
      message.success(t('worldFilter.saved', { name: created.name }));
      onChange(where, created.id);
    } catch (error) {
      message.error(t(savedFilterSaveFailedKey(error)));
    }
  };

  return (
    <>
      <Flex gap={8} wrap align="center" style={{ marginBottom: 12 }}>
        <Button
          icon={<FilterOutlined />}
          onClick={() => setOpen(true)}
          aria-haspopup="dialog"
          data-testid={`${testIdPrefix}-advanced`}
        >
          {t('worldFilter.advanced')}
          {where !== null && ` · ${t('worldFilter.on')}`}
        </Button>
        <Select
          style={{ width: 240 }}
          placeholder={t('worldFilter.applySaved')}
          aria-label={t('worldFilter.applySaved')}
          value={null}
          data-testid={`${testIdPrefix}-apply-saved`}
          showSearch
          optionFilterProp="label"
          notFoundContent={t('worldFilter.noSaved')}
          options={(savedFilters ?? []).map((filter) => ({
            value: filter.id,
            label: filter.name,
            disabled: !appliesTo(filter, world),
          }))}
          onChange={(id: string | null) => {
            if (id) apply(id);
          }}
        />
        <Button
          icon={<SaveOutlined />}
          disabled={document === null}
          data-testid={`${testIdPrefix}-save-filter`}
          onClick={() => setSaving(true)}
        >
          {t('worldFilter.saveAs')}
        </Button>
        <Button
          type="link"
          icon={<SettingOutlined />}
          data-testid={`${testIdPrefix}-manage-filters`}
          onClick={() => navigate('/saved-filters')}
        >
          {t('worldFilter.manage')}
        </Button>
        {savedFilterId !== undefined && (
          <Tag
            color={changed ? 'gold' : 'blue'}
            data-testid={`${testIdPrefix}-active-filter`}
            closable
            closeIcon={<CloseOutlined aria-label={t('worldFilter.forget')} />}
            onClose={(event) => {
              event.preventDefault();
              onChange(where, undefined);
            }}
          >
            {active
              ? t('worldFilter.active', { name: active.name })
              : isSuccess
                ? t('worldFilter.activeUnavailable')
                : '…'}
            {changed && <span data-testid={`${testIdPrefix}-active-changed`}>{` · ${t('worldFilter.changed')}`}</span>}
          </Tag>
        )}
        {active?.state === 'stale' && (
          <Tooltip title={t('savedFilters.staleHint')}>
            <Tag color="orange" data-testid={`${testIdPrefix}-active-stale`}>
              {t('savedFilters.states.stale')}
            </Tag>
          </Tooltip>
        )}
      </Flex>

      {unreadableLink && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid={`${testIdPrefix}-unreadable-link`}
          title={t('worldFilter.unreadableLink')}
        />
      )}
      {refused && (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid={`${testIdPrefix}-filter-refused`}
          title={t('worldFilter.refused')}
        />
      )}

      <Drawer open={open} title={t(titleKey)} size="large" destroyOnHidden onClose={() => setOpen(false)}>
        {/* Keyed by the question it opens on, so an address that moves while it is open re-seeds it
            rather than leaving a draft of an older question to be applied over the newer one. */}
        <FilterEditor
          key={JSON.stringify(where)}
          world={world}
          document={worldEditorDocument(world, where)}
          onCancel={() => setOpen(false)}
          onApply={(edited) => {
            onChange(readWorldDocument(edited, world) ?? null, savedFilterId);
            setOpen(false);
          }}
        />
      </Drawer>

      <Modal
        open={saving}
        title={t('worldFilter.saveTitle')}
        okText={t('common.save')}
        confirmLoading={create.isPending}
        onOk={() => void save()}
        onCancel={() => setSaving(false)}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" preserve={false} initialValues={{ audience: defaultAudience }}>
          <Form.Item
            name="name"
            label={t('savedFilters.name')}
            rules={[{ required: true, whitespace: true, max: 200 }]}
          >
            <Input data-testid={`${testIdPrefix}-saved-filter-name`} autoFocus />
          </Form.Item>
          <Form.Item name="description" label={t('savedFilters.description')} rules={[{ max: 2000 }]}>
            <Input.TextArea rows={3} data-testid={`${testIdPrefix}-saved-filter-description`} />
          </Form.Item>
          <Form.Item name="audience" label={t('savedFilters.audience')}>
            <SavedFilterAudienceField
              groups={groups}
              otherGroups={otherGroups}
              showDefaultLink
              testId={`${testIdPrefix}-saved-filter-audience`}
            />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
