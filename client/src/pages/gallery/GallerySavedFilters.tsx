// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { CloseOutlined, SaveOutlined, SettingOutlined } from '@ant-design/icons';
import { App, Form, Input, Modal, Select, Tag, Tooltip } from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import {
  documentOf,
  useCreateSavedFilter,
  useSavedFilters,
  type SavedFilter,
} from '../../api/hooks/savedFilters.ts';
import SavedFilterAudienceField from '../../components/filters/SavedFilterAudienceField.tsx';
import { savedFilterSaveFailedKey, useSavedFilterAudienceChoice } from '../../components/filters/useSavedFilterAudienceChoice.ts';
import { audienceFields, type SavedFilterAudience } from '../../filters/savedFilterAudience.ts';
import { galleryDocument, readGalleryDocument, sameGalleryQuestion } from '../../filters/gallery.ts';
import type { FilterNode } from '../../filters/types.ts';

interface Props {
  /** The photograph world's conditions the gallery is showing now. */
  where: FilterNode | null;
  /** The saved filter the conditions were taken from, if any. */
  savedFilterId?: string;
  onApply: (where: FilterNode | null, savedFilterId: string) => void;
  onSaved: (savedFilterId: string) => void;
  /** Stops naming a saved filter, keeping the conditions as they are. */
  onForget: () => void;
}

/**
 * Saved gallery views: apply a saved photograph filter, see which one is applied and whether the
 * conditions have moved since, and save the conditions on screen as a new one.
 *
 * Applying a filter only sets the conditions. The gallery is then asked with them under this
 * account's own access, so a filter somebody shared shows exactly the pictures this person could
 * have found by writing the same conditions by hand — nothing more.
 */
export default function GallerySavedFilters({ where, savedFilterId, onApply, onSaved, onForget }: Props) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const { data: savedFilters, isSuccess } = useSavedFilters();
  const create = useCreateSavedFilter();
  const [saving, setSaving] = useState(false);
  const [form] = Form.useForm<{ name: string; description?: string; audience: SavedFilterAudience }>();
  // The dialog is rebuilt on every open, so it starts each time from the member's current default.
  const { groups, otherGroups, defaultAudience } = useSavedFilterAudienceChoice();

  const document = galleryDocument(where);
  const active = savedFilterId === undefined
    ? undefined
    : savedFilters?.find((filter) => filter.id === savedFilterId);
  const activeWhere = active ? readableAsPhotographs(active) : undefined;
  const changed = activeWhere !== undefined && !sameGalleryQuestion(activeWhere, where);

  const apply = (id: string) => {
    const filter = savedFilters?.find((candidate) => candidate.id === id);
    if (!filter) return;
    if (filter.state === 'unreadable') {
      message.warning(t('gallery.filter.savedUnreadable'));
      return;
    }
    const read = readableAsPhotographs(filter);
    if (read === undefined) {
      message.warning(t('gallery.filter.savedNotAboutPhotographs'));
      return;
    }
    onApply(read, filter.id);
  };

  const save = async () => {
    // Never a Save that does nothing: whatever stops it is said.
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
      message.success(t('gallery.filter.saved', { name: created.name }));
      onSaved(created.id);
    } catch (error) {
      message.error(t(savedFilterSaveFailedKey(error)));
    }
  };

  return (
    <>
      <Select
        style={{ width: 240 }}
        placeholder={t('gallery.filter.applySaved')}
        value={null}
        data-testid="gallery-apply-saved"
        showSearch
        optionFilterProp="label"
        notFoundContent={t('gallery.filter.noSaved')}
        options={(savedFilters ?? []).map((filter) => ({
          value: filter.id,
          label: filter.name,
          disabled: filter.state === 'unreadable',
        }))}
        onChange={(id: string | null) => {
          if (id) apply(id);
        }}
      />
      <Button
        icon={<SaveOutlined />}
        disabled={document === null}
        data-testid="gallery-save-filter"
        onClick={() => setSaving(true)}
      >
        {t('gallery.filter.saveAs')}
      </Button>
      <Button
        type="link"
        icon={<SettingOutlined />}
        data-testid="gallery-manage-filters"
        onClick={() => navigate('/saved-filters')}
      >
        {t('gallery.filter.manage')}
      </Button>
      {savedFilterId !== undefined && (
        <Tag
          color={changed ? 'gold' : 'blue'}
          data-testid="gallery-active-filter"
          closable
          closeIcon={<CloseOutlined aria-label={t('gallery.filter.forget')} />}
          onClose={(event) => {
            event.preventDefault();
            onForget();
          }}
        >
          {active
            ? t('gallery.filter.active', { name: active.name })
            : isSuccess
              ? t('gallery.filter.activeUnavailable')
              : '…'}
          {changed && (
            <span data-testid="gallery-active-changed">{` · ${t('gallery.filter.changed')}`}</span>
          )}
        </Tag>
      )}
      {active?.state === 'stale' && (
        <Tooltip title={t('savedFilters.staleHint')}>
          <Tag color="orange" data-testid="gallery-active-stale">
            {t('savedFilters.states.stale')}
          </Tag>
        </Tooltip>
      )}
      <Modal
        open={saving}
        title={t('gallery.filter.saveTitle')}
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
            <Input data-testid="gallery-saved-filter-name" autoFocus />
          </Form.Item>
          <Form.Item name="description" label={t('savedFilters.description')} rules={[{ max: 2000 }]}>
            <Input.TextArea rows={3} data-testid="gallery-saved-filter-description" />
          </Form.Item>
          <Form.Item name="audience" label={t('savedFilters.audience')}>
            <SavedFilterAudienceField groups={groups} otherGroups={otherGroups} showDefaultLink testId="gallery-saved-filter-audience" />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}

/** A saved filter as the gallery can hold it, or undefined when it is not about photographs. */
function readableAsPhotographs(filter: SavedFilter): FilterNode | null | undefined {
  const document = documentOf(filter);
  return filter.state === 'unreadable' || document === null ? undefined : readGalleryDocument(document);
}
